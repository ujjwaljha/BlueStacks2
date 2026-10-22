using System.Net.Sockets;
using NexusVM.Core.Models;

namespace NexusVM.Core.Protocol;

/// <summary>
/// Polls QEMU screendump over the loopback QMP socket and raises BGRA frames.
/// This is the macOS embed path: Cocoa will not adopt a foreign QEMU window.
/// </summary>
public sealed class QmpFramePump : IAsyncDisposable
{
    private readonly TimeSpan _interval;
    private readonly TimeSpan _connectTimeout;
    private QmpClient? _client;

    public QmpFramePump(TimeSpan? frameInterval = null, TimeSpan? connectTimeout = null)
    {
        _interval = frameInterval ?? TimeSpan.FromMilliseconds(400);
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(8);
    }

    public event EventHandler<PixelFrame>? FrameArrived;

    public event EventHandler<string>? Faulted;

    public async Task RunAsync(int port, string outputDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var framePath = Path.GetFullPath(Path.Combine(outputDirectory, "frame.ppm"));
        _client = new QmpClient();

        try
        {
            await ConnectWithRetryAsync(port, cancellationToken).ConfigureAwait(false);
            var arguments = new Dictionary<string, string> { ["filename"] = framePath };
            while (!cancellationToken.IsCancellationRequested)
            {
                _ = await _client.ExecuteAsync("screendump", arguments, cancellationToken).ConfigureAwait(false);
                if (PpmImageReader.TryRead(framePath, out var frame) && frame is not null)
                    FrameArrived?.Invoke(this, frame);

                await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Session stop.
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or TimeoutException)
        {
            Faulted?.Invoke(this, ex.Message);
        }
        finally
        {
            if (_client is not null)
                await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync().ConfigureAwait(false);
        _client = null;
    }

    private async Task ConnectWithRetryAsync(int port, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _connectTimeout;
        Exception? last = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _client!.ConnectAsync(port, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or TimeoutException)
            {
                last = ex;
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException($"Timed out waiting for QMP on 127.0.0.1:{port}.", last);

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                await _client!.DisposeAsync().ConfigureAwait(false);
                _client = new QmpClient();
            }
        }
    }
}
