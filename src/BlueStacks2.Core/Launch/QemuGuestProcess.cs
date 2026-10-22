using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using BlueStacks2.Core.Models;
using BlueStacks2.Core.Protocol;

namespace BlueStacks2.Core.Launch;

public sealed class QemuGuestProcess : IAsyncDisposable
{
    private const int LogCap = 65536;

    private readonly Process _process;
    private readonly StringBuilder _log = new();
    private readonly object _exitGate = new();
    private EventHandler<int>? _exited;
    private int? _exitCode;
    private int _disposed;

    private QemuGuestProcess(Process process, QemuLaunchPlan plan)
    {
        _process = process;
        Plan = plan;
    }

    public QemuLaunchPlan Plan { get; }

    public int ProcessId => _process.Id;

    public bool HasExited => _process.HasExited;

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

    public static QemuGuestProcess Start(string binaryPath, QemuLaunchPlan plan, string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);
        ArgumentNullException.ThrowIfNull(plan);
        ValidateExecutable(binaryPath, plan.Binary);

        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            startInfo.WorkingDirectory = workingDirectory;

        foreach (var argument in plan.Arguments)
            startInfo.ArgumentList.Add(argument);

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        var guest = new QemuGuestProcess(process, plan);
        process.ErrorDataReceived += (_, args) => guest.AppendLog(args.Data);
        process.OutputDataReceived += (_, args) => guest.AppendLog(args.Data);
        process.Exited += (_, _) =>
        {
            int code;
            try
            {
                code = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                code = -1;
            }

            EventHandler<int>? handler;
            lock (guest._exitGate)
            {
                guest._exitCode = code;
                handler = guest._exited;
            }

            handler?.Invoke(guest, code);
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Process.Start returned false.");

            try
            {
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
            }
            catch (InvalidOperationException)
            {
                // The process exited before the asynchronous readers were attached.
            }
        }
        catch (Exception ex) when (ex is not AccelerationDiagnosticException)
        {
            process.Dispose();
            throw new AccelerationDiagnosticException(
                "QEMU failed to start",
                $"The process '{binaryPath}' could not be started.",
                "Confirm the file is executable and that this QEMU build supports the selected accelerator.",
                new[] { ex.Message },
                ex);
        }

        return guest;
    }

    public string DrainLog()
    {
        lock (_log)
            return _log.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            if (!_process.HasExited)
            {
                await TryQuitAsync().ConfigureAwait(false);
                if (!_process.HasExited && !_process.WaitForExit(1500))
                    _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // already gone
            }
        }
        finally
        {
            _process.Dispose();
        }
    }

    private async Task TryQuitAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var client = new QmpClient();
            await client.ConnectAsync(Plan.QmpPort, timeout.Token).ConfigureAwait(false);
            _ = await client.ExecuteAsync("quit", null, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or InvalidOperationException or OperationCanceledException)
        {
            // QMP is optional. The caller kills the process if it is still running.
        }
    }

    private void AppendLog(string? line)
    {
        if (line is null)
            return;

        lock (_log)
        {
            if (_log.Length >= LogCap)
                return;

            _log.AppendLine(line);
        }
    }

    private static void ValidateExecutable(string binaryPath, string expectedName)
    {
        if (!File.Exists(binaryPath))
        {
            throw new AccelerationDiagnosticException(
                "QEMU was not found",
                $"The resolved QEMU binary does not exist: {binaryPath}",
                "Install QEMU and run the host check again.");
        }

        var fileName = Path.GetFileName(binaryPath);
        if (!fileName.Equals(expectedName, StringComparison.OrdinalIgnoreCase) &&
            !fileName.Equals(expectedName + ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new AccelerationDiagnosticException(
                "QEMU binary was rejected",
                $"Refusing to launch '{fileName}'. The launch plan requires '{expectedName}'.",
                "The shell only starts qemu-system-x86_64 or qemu-system-aarch64.");
        }
    }
}
