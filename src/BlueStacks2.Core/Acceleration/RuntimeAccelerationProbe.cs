using BlueStacks2.Core.Models;

namespace BlueStacks2.Core.Acceleration;

public sealed class RuntimeAccelerationProbe : IAccelerationProbe
{
    private readonly LinuxKvmProbe _linux;
    private readonly WindowsWhpxProbe _windows;
    private readonly MacOsHvfProbe _macOS;

    public RuntimeAccelerationProbe()
        : this(new LinuxKvmProbe(), new WindowsWhpxProbe(), new MacOsHvfProbe())
    {
    }

    public RuntimeAccelerationProbe(LinuxKvmProbe linux, WindowsWhpxProbe windows, MacOsHvfProbe macOS)
    {
        _linux = linux;
        _windows = windows;
        _macOS = macOS;
    }

    public AccelerationProbeResult Probe(HostEnvironmentSnapshot environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return environment.Platform switch
        {
            HostPlatformKind.WindowsX64 => _windows.Probe(),
            HostPlatformKind.LinuxX64 => _linux.Probe(),
            HostPlatformKind.MacOsArm64 => _macOS.Probe(),
            _ => HostCompatibility.ForUnsupported(environment),
        };
    }
}
