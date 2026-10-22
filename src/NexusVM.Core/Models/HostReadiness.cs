namespace NexusVM.Core.Models;

public sealed record HostReadiness(
    HostEnvironmentSnapshot Environment,
    AccelerationProbeResult Acceleration);

public sealed record PreparedLaunch(
    HostReadiness Readiness,
    QemuLaunchPlan Plan,
    string ExecutablePath);
