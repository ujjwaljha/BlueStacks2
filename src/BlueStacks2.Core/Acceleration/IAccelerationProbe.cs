using BlueStacks2.Core.Models;

namespace BlueStacks2.Core.Acceleration;

public interface IAccelerationProbe
{
    AccelerationProbeResult Probe(HostEnvironmentSnapshot environment);
}
