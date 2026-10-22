using System.ComponentModel;
using System.Diagnostics;
using BlueStacks2.Core.Models;

namespace BlueStacks2.Core.Acceleration;

public interface ISystemControl
{
    bool TryRead(string key, out string stdout, out string stderr, out int exitCode);
}

public sealed class ProcessSystemControl : ISystemControl
{
    public bool TryRead(string key, out string stdout, out string stderr, out int exitCode)
    {
        stdout = string.Empty;
        stderr = string.Empty;
        exitCode = -1;

        if (string.IsNullOrWhiteSpace(key) || !IsSafeKey(key))
        {
            stderr = "The sysctl key was rejected.";
            return false;
        }

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "/usr/sbin/sysctl",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            process.StartInfo.ArgumentList.Add("-n");
            process.StartInfo.ArgumentList.Add(key);

            if (!process.Start())
                return false;

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(2000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // already exited
                }

                stderr = "sysctl did not exit within 2 seconds.";
                return false;
            }

            stdout = outputTask.GetAwaiter().GetResult();
            stderr = errorTask.GetAwaiter().GetResult();
            exitCode = process.ExitCode;
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            stderr = ex.Message;
            return false;
        }
    }

    private static bool IsSafeKey(string key)
    {
        foreach (var character in key)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '.' or '_'))
                return false;
        }

        return true;
    }
}

public sealed class MacOsHvfProbe
{
    public const string SupportKey = "kern.hv_support";

    private readonly ISystemControl _systemControl;

    public MacOsHvfProbe(ISystemControl? systemControl = null)
    {
        _systemControl = systemControl ?? new ProcessSystemControl();
    }

    public AccelerationProbeResult Probe()
    {
        try
        {
            if (!_systemControl.TryRead(SupportKey, out var stdout, out var stderr, out var exitCode))
            {
                return AccelerationProbeResult.Unavailable(
                    "hvf",
                    "Hypervisor.framework is unavailable",
                    "The launcher could not read kern.hv_support from sysctl, so HVF cannot be selected for the arm64 GSI.",
                    """
                    Run this shell on Apple Silicon. HVF is reported by:
                        sysctl -n kern.hv_support
                    The value must be 1. Intel Macs and virtual machines without Hypervisor.framework are not a supported host.
                    """,
                    Detail(stderr));
            }

            if (exitCode != 0)
            {
                return AccelerationProbeResult.Unavailable(
                    "hvf",
                    "Hypervisor.framework is unavailable",
                    $"sysctl -n kern.hv_support exited with code {exitCode}. HVF is not available for the arm64 GSI.",
                    "Confirm this Mac is Apple Silicon and that Hypervisor.framework is enabled (kern.hv_support must be 1).",
                    Detail(stderr));
            }

            if (string.Equals(stdout.Trim(), "1", StringComparison.Ordinal))
            {
                return AccelerationProbeResult.Available(
                    "hvf",
                    "Hypervisor.framework reports kern.hv_support=1.");
            }

            return AccelerationProbeResult.Unavailable(
                "hvf",
                "Hypervisor.framework is unavailable",
                $"kern.hv_support returned '{stdout.Trim()}'. Hypervisor.framework is not available, so the arm64 GSI cannot use HVF.",
                """
                HVF requires an Apple Silicon Mac with Hypervisor.framework enabled.
                Check:
                    sysctl -n kern.hv_support
                A value of 1 is required. This shell does not fall back to TCG for the aarch64 guest.
                """,
                Detail("stdout=" + stdout.Trim()));
        }
        catch (Exception ex)
        {
            return AccelerationProbeResult.Unavailable(
                "hvf",
                "Hypervisor.framework probe failed",
                "The HVF probe hit an unexpected error and will not launch QEMU.",
                "Confirm sysctl -n kern.hv_support prints 1 on this Apple Silicon Mac.",
                Detail(ex.GetType().Name + ": " + ex.Message));
        }
    }

    private static IReadOnlyList<string> Detail(string? line) =>
        string.IsNullOrWhiteSpace(line) ? Array.Empty<string>() : new[] { line };
}
