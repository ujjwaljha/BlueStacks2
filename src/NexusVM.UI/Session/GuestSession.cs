using NexusVM.UI.Views;
using NexusVM.Core.Engine;
using NexusVM.Core.Models;
using NexusVM.Core.Protocol;

namespace NexusVM.UI.Session;

public sealed class GuestSession : IAsyncDisposable
{
    private readonly QemuGuestProcess _process;
    private readonly CancellationTokenSource _frames = new();
    private readonly string _directory;
    private readonly object _exitGate = new();
    private EventHandler<int>? _exited;
    private int? _exitCode;
    private Task? _pumpTask;
    private int _disposed;

    private GuestSession(QemuGuestProcess process, string directory)
    {
        _process = process;
        _directory = directory;
        ProcessId = process.ProcessId;
    }

    public int ProcessId { get; }

    public int AdbHostPort => _process.Plan.AdbHostPort;

    public event EventHandler<string>? LogReceived
    {
        add => _process.LogReceived += value;
        remove => _process.LogReceived -= value;
    }

    public event EventHandler<int>? Exited
    {
        add
        {
            int? pending;
            lock (_exitGate)
            {
                _exited += value;
                pending = _exitCode;
            }

            if (pending is int code && value is not null)
                value(this, code);
        }
        remove
        {
            lock (_exitGate)
                _exited -= value;
        }
    }

    public static GuestSession Start(PreparedLaunch launch, QemuSurfaceView surface)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(surface);

        var directory = Path.Combine(Path.GetTempPath(), "nexusvm", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var process = QemuGuestProcess.Start(launch.ExecutablePath, launch.Plan, directory);
        var session = new GuestSession(process, directory);
        process.Exited += (_, code) =>
        {
            EventHandler<int>? handler;
            lock (session._exitGate)
            {
                session._exitCode = code;
                handler = session._exited;
            }

            handler?.Invoke(session, code);
        };

        try
        {
            session.AttachSurface(launch, surface);
            return session;
        }
        catch
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    public string ReadLog() => _process.DrainLog();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _frames.Cancel();
        if (_pumpTask is not null)
        {
            try
            {
                await _pumpTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The pump reports capture failures on the surface.
            }
        }

        await _process.DisposeAsync().ConfigureAwait(false);

        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A frame file may still be open.
        }
        catch (UnauthorizedAccessException)
        {
            // The temp directory is left for the OS to reclaim.
        }
    }

    private void AttachSurface(PreparedLaunch launch, QemuSurfaceView surface)
    {
        if (launch.Plan.DisplayMode == DisplayEmbedMode.NativeWindow)
        {
            surface.BeginNativeEmbed(ProcessId, launch.Plan.WindowTitle);
            return;
        }

        if (launch.Plan.DisplayMode != DisplayEmbedMode.Framebuffer)
            return;

        var pump = new QmpFramePump();
        pump.FrameArrived += (_, frame) => surface.PresentFrame(frame);
        pump.Faulted += (_, message) => surface.ShowFault(
            "Framebuffer capture failed",
            message + " The guest process is still running. QMP screendump is the macOS embed path.");
        _pumpTask = pump.RunAsync(launch.Plan.QmpPort, _directory, _frames.Token);
    }
}
