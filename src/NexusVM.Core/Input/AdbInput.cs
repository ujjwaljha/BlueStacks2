using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace NexusVM.Core.Input;

public static class AdbInput
{
    public const int GuestPort = 5555;
    public const int SwipeDurationMs = 150;

    public static string LoopbackSerial(int hostPort)
    {
        if (hostPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(hostPort));

        return "127.0.0.1:" + hostPort.ToString(CultureInfo.InvariantCulture);
    }

    public static bool IsLoopbackSerial(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return false;

        const string prefix = "127.0.0.1:";
        if (!serial.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        return int.TryParse(serial[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && port is >= 1 and <= 65535
            && serial == LoopbackSerial(port);
    }

    public static IReadOnlyList<string> ConnectCommand(string serial)
    {
        if (!IsLoopbackSerial(serial))
            throw new ArgumentException("ADB serial must be 127.0.0.1 and a port.", nameof(serial));

        return new[] { "connect", serial };
    }

    public static IReadOnlyList<string> DisconnectCommand(string serial)
    {
        if (!IsLoopbackSerial(serial))
            throw new ArgumentException("ADB serial must be 127.0.0.1 and a port.", nameof(serial));

        return new[] { "disconnect", serial };
    }

    public static void EnsureSafe(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 2 && arguments[0] is "connect" or "disconnect" && IsLoopbackSerial(arguments[1]))
            return;

        var input = arguments.Count == 7 && arguments[4] == "tap"
            || arguments.Count == 10 && arguments[4] == "swipe";
        if (input
            && arguments[0] == "-s"
            && IsLoopbackSerial(arguments[1])
            && arguments[2] == "shell"
            && arguments[3] == "input"
            && arguments.Skip(5).All(IsCoordinate))
        {
            return;
        }

        throw new ArgumentException("ADB arguments must be a loopback connect, disconnect, or input command.", nameof(arguments));
    }

    private static bool IsCoordinate(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 0 and <= 10000;

    public static IReadOnlyList<string> Command(string serial, KeyMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (!IsLoopbackSerial(serial))
            throw new ArgumentException("ADB serial must be 127.0.0.1 and a port.", nameof(serial));

        var arguments = new List<string> { "-s", serial };
        arguments.AddRange(InputArguments(mapping));
        return arguments;
    }

    public static IReadOnlyList<string> InputArguments(KeyMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        var culture = CultureInfo.InvariantCulture;
        return mapping.Action switch
        {
            "tap" => new[]
            {
                "shell", "input", "tap",
                mapping.TargetX.ToString(culture),
                mapping.TargetY.ToString(culture),
            },
            "swipe" => new[]
            {
                "shell", "input", "swipe",
                mapping.StartX.ToString(culture),
                mapping.StartY.ToString(culture),
                mapping.EndX.ToString(culture),
                mapping.EndY.ToString(culture),
                SwipeDurationMs.ToString(culture),
            },
            _ => throw new ArgumentException("Mapping action must be tap or swipe.", nameof(mapping)),
        };
    }
}

public readonly record struct AdbResult(int ExitCode, string Output);

/// <summary>
/// Runs the adb binary from PATH. The executable name is fixed. Arguments come from <see cref="AdbInput"/>.
/// </summary>
public sealed class AdbProcessClient
{
    private readonly string _fileName;

    public AdbProcessClient(string fileName = "adb")
    {
        if (fileName is not ("adb" or "adb.exe"))
            throw new ArgumentException("Only the adb executable name is accepted.", nameof(fileName));

        _fileName = fileName;
    }

    public async Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        AdbInput.EnsureSafe(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = _fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("adb did not start.");
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new InvalidOperationException("adb was not found on PATH.", ex);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // already exited
                }

                throw;
            }
            var output = new StringBuilder();
            var error = await stderr.ConfigureAwait(false);
            var standard = await stdout.ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(error))
                output.AppendLine(error.Trim());
            if (!string.IsNullOrWhiteSpace(standard))
                output.AppendLine(standard.Trim());
            return new AdbResult(process.ExitCode, output.ToString().Trim());
        }
    }
}
