using BlueStacks2.Core.Acceleration;
using BlueStacks2.Core.Detection;
using BlueStacks2.Core.Models;

namespace BlueStacks2.Core.Launch;

public sealed class HostOrchestrator
{
    private readonly EnvironmentDetector _detector;
    private readonly IAccelerationProbe _probe;
    private readonly QemuCommandBuilder _builder;
    private readonly IQemuBinaryLocator _locator;

    public HostOrchestrator(
        EnvironmentDetector detector,
        IAccelerationProbe probe,
        QemuCommandBuilder builder,
        IQemuBinaryLocator locator)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
    }

    public static HostOrchestrator CreateDefault() =>
        new(
            new EnvironmentDetector(),
            new RuntimeAccelerationProbe(),
            new QemuCommandBuilder(),
            new PathQemuBinaryLocator());

    public HostReadiness Inspect()
    {
        var environment = _detector.Detect();
        var acceleration = _probe.Probe(environment);
        return new HostReadiness(environment, acceleration);
    }

    public PreparedLaunch Prepare(GuestLaunchRequest request)
    {
        var readiness = Inspect();
        var plan = _builder.Build(readiness.Environment, readiness.Acceleration, request);
        var executable = _locator.Locate(plan.Binary);
        if (executable is null)
        {
            throw new AccelerationDiagnosticException(
                "QEMU was not found",
                $"The launcher could not find '{plan.Binary}' on PATH.",
                InstallHint(readiness.Environment.Platform),
                new[] { "binary=" + plan.Binary });
        }

        return new PreparedLaunch(readiness, plan, executable);
    }

    private static string InstallHint(HostPlatformKind platform) => platform switch
    {
        HostPlatformKind.WindowsX64 =>
            "Install QEMU for Windows and add its directory to PATH. The binary name is qemu-system-x86_64.exe.",
        HostPlatformKind.LinuxX64 =>
            "Install the distro qemu-system-x86 package so qemu-system-x86_64 is on PATH.",
        HostPlatformKind.MacOsArm64 =>
            "Install QEMU (for example from Homebrew) so qemu-system-aarch64 is on PATH.",
        _ => "Install QEMU and ensure the matching system binary is on PATH.",
    };
}
