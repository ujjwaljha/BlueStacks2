using BlueStacks2.Core.Models;

namespace BlueStacks2.Core.Acceleration;

public sealed class LinuxKvmProbe
{
    public const string DefaultDevicePath = "/dev/kvm";

    private readonly string _devicePath;
    private readonly Func<string, bool> _exists;
    private readonly Func<string, Stream> _openReadWrite;
    private readonly Func<string?> _readCpuInfo;
    private readonly Func<string, string?> _describeMode;

    public LinuxKvmProbe(
        string devicePath = DefaultDevicePath,
        Func<string, bool>? exists = null,
        Func<string, Stream>? openReadWrite = null,
        Func<string?>? readCpuInfo = null,
        Func<string, string?>? describeMode = null)
    {
        _devicePath = string.IsNullOrWhiteSpace(devicePath) ? DefaultDevicePath : devicePath;
        _exists = exists ?? File.Exists;
        _openReadWrite = openReadWrite ?? OpenDevice;
        _readCpuInfo = readCpuInfo ?? ReadProcCpuInfo;
        _describeMode = describeMode ?? DescribeUnixMode;
    }

    public AccelerationProbeResult Probe()
    {
        try
        {
            if (!_exists(_devicePath))
                return MissingDevice();

            try
            {
                using var stream = _openReadWrite(_devicePath);
                _ = stream;
            }
            catch (Exception ex) when (IsAccessDenied(ex))
            {
                return PermissionDenied(ex);
            }
            catch (Exception ex)
            {
                return AccelerationProbeResult.Unavailable(
                    "kvm",
                    "KVM device could not be opened",
                    $"Opening {_devicePath} failed, so the Android-x86_64 guest cannot use KVM.",
                    "Confirm the kvm_intel or kvm_amd module is loaded and that the current user can open /dev/kvm.",
                    Detail(ex.GetType().Name + ": " + ex.Message));
            }

            return AccelerationProbeResult.Available(
                "kvm",
                $"KVM is available. {_devicePath} opened for read/write.");
        }
        catch (Exception ex)
        {
            return AccelerationProbeResult.Unavailable(
                "kvm",
                "KVM probe failed",
                "The KVM probe hit an unexpected error and will not launch QEMU.",
                "Confirm /dev/kvm exists and that this user is in the kvm group.",
                Detail(ex.GetType().Name + ": " + ex.Message));
        }
    }

    private AccelerationProbeResult MissingDevice()
    {
        var cpu = _readCpuInfo() ?? string.Empty;
        var hasVmx = cpu.Contains("vmx", StringComparison.Ordinal);
        var hasSvm = cpu.Contains("svm", StringComparison.Ordinal);
        var cpuDetail = hasVmx || hasSvm
            ? "CPU flags advertise virtualization (vmx or svm), but the /dev/kvm device node is missing."
            : "CPU flags do not list vmx or svm. Firmware virtualization may be disabled.";

        return AccelerationProbeResult.Unavailable(
            "kvm",
            "KVM device is missing",
            $"{_devicePath} is not present, so KVM acceleration cannot start the Android-x86_64 guest.",
            """
            Load the KVM module and confirm firmware virtualization is enabled:
                sudo modprobe kvm_intel
            or, on AMD:
                sudo modprobe kvm_amd
            Then confirm /dev/kvm exists. If the CPU flags do not list vmx or svm, enable Intel VT-x or AMD-V in firmware.
            """,
            Detail(cpuDetail));
    }

    private AccelerationProbeResult PermissionDenied(Exception exception)
    {
        var details = new List<string>
        {
            "user=" + Environment.UserName,
            "exception=" + exception.GetType().Name + ": " + exception.Message,
        };

        var mode = _describeMode(_devicePath);
        if (!string.IsNullOrWhiteSpace(mode))
            details.Add("unixMode=" + mode);

        var groups = ReadProcStatusLine("Groups:");
        if (!string.IsNullOrWhiteSpace(groups))
            details.Add(groups);

        return AccelerationProbeResult.Unavailable(
            "kvm",
            "KVM permission denied",
            $"The kernel device {_devicePath} exists, but this process cannot open it. Hardware acceleration for the Android-x86_64 guest is unavailable.",
            """
            Add the current user to the kvm group, then start a new login session:
                sudo usermod -aG kvm $USER
            /dev/kvm is normally a character device owned by root:kvm with mode crw-rw----.
            """,
            details);
    }

    private static List<string> Detail(string line) => new() { line };

    private static bool IsAccessDenied(Exception exception)
    {
        if (exception is UnauthorizedAccessException or System.Security.SecurityException)
            return true;

        var errno = exception.HResult & 0xFFFF;
        return errno is 13 or 1 or 5;
    }

    private static Stream OpenDevice(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

    private static string? ReadProcCpuInfo()
    {
        try
        {
            return File.Exists("/proc/cpuinfo") ? File.ReadAllText("/proc/cpuinfo") : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? DescribeUnixMode(string path)
    {
        if (!OperatingSystem.IsLinux())
            return null;

        try
        {
            return File.GetUnixFileMode(path).ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ReadProcStatusLine(string prefix)
    {
        try
        {
            if (!File.Exists("/proc/self/status"))
                return null;

            foreach (var line in File.ReadLines("/proc/self/status"))
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                    return line;
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }
}
