using NexusVM.Core.Models;

namespace NexusVM.Core.Acceleration;

public interface IAccelerationProbe
{
    AccelerationProbeResult Probe(HostEnvironmentSnapshot environment);
}
