using System.Runtime.InteropServices;
using NexusVM.Core.Models;

namespace NexusVM.Core.Acceleration;

public interface IWhpxCapabilitySource
{
    bool IsHypervisorPlatformLibraryPresent { get; }

    /// <summary>
    /// Calls WHvGetCapability(WHvCapabilityCodeHypervisorPresent).
    /// Returns false when the platform library cannot be queried.
    /// </summary>
    bool TryQueryHypervisorPresent(out int presentFlag, out int hresult);

    /// <summary>
    /// PF_VIRT_FIRMWARE_ENABLED when the host is Windows; otherwise null.
    /// </summary>
    bool? IsFirmwareVirtualizationEnabled { get; }
}

public sealed class WindowsWhpxCapabilitySource : IWhpxCapabilitySource
{
    private const int HypervisorPresentCode = 0;
    private const int FirmwareVirtualizationFeature = 21;

    public bool IsHypervisorPlatformLibraryPresent
    {
        get
        {
            if (!OperatingSystem.IsWindows())
                return false;

            if (!NativeLibrary.TryLoad("WinHvPlatform.dll", out var handle))
                return false;

            NativeLibrary.Free(handle);
            return true;
        }
    }

    public bool TryQueryHypervisorPresent(out int presentFlag, out int hresult)
    {
        presentFlag = 0;
        hresult = unchecked((int)0x80070002);
        if (!OperatingSystem.IsWindows())
            return false;

        if (!NativeLibrary.TryLoad("WinHvPlatform.dll", out var library))
            return false;

        try
        {
            if (!NativeLibrary.TryGetExport(library, "WHvGetCapability", out var export))
                return false;

            var query = Marshal.GetDelegateForFunctionPointer<WHvGetCapabilityDelegate>(export);
            hresult = query(HypervisorPresentCode, out presentFlag, sizeof(int), out _);
            return true;
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    public bool? IsFirmwareVirtualizationEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows())
                return null;

            return IsProcessorFeaturePresent(FirmwareVirtualizationFeature);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int WHvGetCapabilityDelegate(
        int capabilityCode,
        out int capabilityBuffer,
        uint capabilityBufferSizeInBytes,
        out uint writtenSizeInBytes);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool IsProcessorFeaturePresent(int processorFeature);
}

public sealed class WindowsWhpxProbe
{
    private readonly IWhpxCapabilitySource _source;

    public WindowsWhpxProbe(IWhpxCapabilitySource? source = null)
    {
        _source = source ?? new WindowsWhpxCapabilitySource();
    }

    public AccelerationProbeResult Probe()
    {
        try
        {
            var firmware = FirmwareDetail(_source.IsFirmwareVirtualizationEnabled);

            if (!_source.IsHypervisorPlatformLibraryPresent)
            {
                return AccelerationProbeResult.Unavailable(
                    "whpx",
                    "Windows Hypervisor Platform is disabled",
                    "WHPX is required for the Android-x86_64 guest, but WinHvPlatform.dll did not load. The Windows Optional Feature is turned off or is not installed.",
                    """
                    1. Open "Turn Windows features on or off".
                    2. Enable "Windows Hypervisor Platform".
                    3. Reboot.
                    4. Confirm CPU virtualization (Intel VT-x or AMD-V) is enabled in firmware.
                    Administrator command:
                        dism /Online /Enable-Feature /FeatureName:HypervisorPlatform /All
                    """,
                    firmware);
            }

            if (!_source.TryQueryHypervisorPresent(out var present, out var hresult))
            {
                return AccelerationProbeResult.Unavailable(
                    "whpx",
                    "Windows Hypervisor Platform is disabled",
                    "WinHvPlatform.dll was reported present, but WHvGetCapability could not be called. The Windows Hypervisor Platform feature is not usable.",
                    """
                    Re-enable "Windows Hypervisor Platform" in Windows Optional Features and reboot.
                        dism /Online /Enable-Feature /FeatureName:HypervisorPlatform /All
                    """,
                    firmware);
            }

            if (hresult < 0)
            {
                var code = "0x" + hresult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
                var details = new List<string> { "hresult=" + code };
                details.AddRange(firmware);
                return AccelerationProbeResult.Unavailable(
                    "whpx",
                    "Windows Hypervisor Platform query failed",
                    $"WHvGetCapability failed with HRESULT {code}. WHPX will not start the Android-x86_64 guest.",
                    """
                    Enable "Windows Hypervisor Platform" in Windows Optional Features, then reboot.
                        dism /Online /Enable-Feature /FeatureName:HypervisorPlatform /All
                    If the feature is already enabled, confirm firmware virtualization and that the hypervisor is allowed to start.
                    """,
                    details);
            }

            if (present == 0)
            {
                return AccelerationProbeResult.Unavailable(
                    "whpx",
                    "Windows hypervisor is not running",
                    "WinHvPlatform.dll loaded, but WHvGetCapability reports the hypervisor is not present. WHPX cannot start the Android-x86_64 guest.",
                    """
                    The Windows Hypervisor Platform feature may be installed while the hypervisor itself is off.
                    1. Enable "Windows Hypervisor Platform" under Windows Optional Features.
                    2. From an elevated command prompt: bcdedit /set hypervisorlaunchtype auto
                    3. Reboot, and confirm Intel VT-x or AMD-V is enabled in firmware.
                    """,
                    firmware);
            }

            return AccelerationProbeResult.Available(
                "whpx",
                "Windows Hypervisor Platform is present and the hypervisor is running.");
        }
        catch (Exception ex)
        {
            return AccelerationProbeResult.Unavailable(
                "whpx",
                "Windows Hypervisor Platform probe failed",
                "The WHPX probe hit an unexpected error and will not launch QEMU.",
                "Enable the Windows Hypervisor Platform optional feature and reboot.",
                new[] { ex.GetType().Name + ": " + ex.Message });
        }
    }

    private static IReadOnlyList<string> FirmwareDetail(bool? enabled)
    {
        if (enabled is null)
            return Array.Empty<string>();

        return new[]
        {
            enabled.Value
                ? "Firmware virtualization (PF_VIRT_FIRMWARE_ENABLED) is reported present."
                : "Firmware virtualization (PF_VIRT_FIRMWARE_ENABLED) is reported absent. Enable VT-x or AMD-V in firmware.",
        };
    }
}
