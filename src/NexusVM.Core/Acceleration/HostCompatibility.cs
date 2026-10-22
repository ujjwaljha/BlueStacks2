using NexusVM.Core.Models;

namespace NexusVM.Core.Acceleration;

public static class HostCompatibility
{
    public static AccelerationProbeResult ForUnsupported(HostEnvironmentSnapshot environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return AccelerationProbeResult.Unavailable(
            HostProfiles.AcceleratorName(HostPlatformKind.Unsupported),
            "Unsupported host configuration",
            $"This host is {environment.Describe()}. NexusVM supports Windows x64 with WHPX, Linux x64 with KVM, and macOS Apple Silicon with HVF.",
            "Use a Windows x64 machine with the Windows Hypervisor Platform optional feature, a Linux x64 machine with /dev/kvm, or an Apple Silicon Mac.",
            new[]
            {
                "os=" + environment.OperatingSystemDescription,
                "osArchitecture=" + environment.OperatingSystemArchitecture,
                "processArchitecture=" + environment.ProcessArchitecture,
            });
    }
}
