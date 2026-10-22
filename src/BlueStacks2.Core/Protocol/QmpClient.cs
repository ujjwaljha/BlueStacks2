using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace BlueStacks2.Core.Protocol;

public sealed class QmpClient : IAsyncDisposable
{
    private readonly byte[] _bytes = new byte[8192];
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly char[] _chars = new char[8192];
    private TcpClient? _client;
    private NetworkStream? _stream;
    private string _pending = string.Empty;

    public async Task ConnectAsync(int port, CancellationToken cancellationToken)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));

        if (_client is not null)
            throw new InvalidOperationException("This QMP client is already connected.");

        _client = new TcpClient { NoDelay = true };
        await _client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
        _stream = _client.GetStream();

        var greeting = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
        if (!greeting.Contains("\"QMP\"", StringComparison.Ordinal))
            throw new InvalidOperationException("QEMU did not send a QMP greeting.");

        _ = await ExecuteAsync("qmp_capabilities", null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ExecuteAsync(
        string command,
        IReadOnlyDictionary<string, string>? arguments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (_stream is null)
            throw new InvalidOperationException("QMP is not connected.");

        var payload = arguments is null || arguments.Count == 0
            ? JsonSerializer.Serialize(new Dictionary<string, object> { ["execute"] = command })
            : JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["execute"] = command,
                ["arguments"] = arguments,
            });

        var bytes = Encoding.UTF8.GetBytes(payload + "\n");
        await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var message = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(message);
            if (document.RootElement.TryGetProperty("event", out _))
                continue;

            if (document.RootElement.TryGetProperty("error", out var error))
                throw new InvalidOperationException("QMP error: " + error.GetRawText());

            return message;
        }

        throw new InvalidOperationException("QMP did not return a result.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
            await _stream.DisposeAsync().ConfigureAwait(false);

        _client?.Dispose();
        _stream = null;
        _client = null;
    }

    private async Task<string> ReadMessageAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = _pending;
            if (QmpMessageReader.TryTake(ref pending, out var json))
            {
                _pending = pending;
                return json;
            }

            var read = await _stream!.ReadAsync(_bytes, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new IOException("The QMP connection closed.");

            var count = _decoder.GetChars(_bytes, 0, read, _chars, 0, flush: false);
            _pending += new string(_chars, 0, count);
            if (_pending.Length > 1_048_576)
                throw new IOException("The QMP buffer exceeded 1 MiB.");
        }
    }
}
