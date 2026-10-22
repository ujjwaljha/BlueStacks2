using System.Runtime.InteropServices;
using NexusVM.Core.Models;

namespace NexusVM.Core.Detectors;

public interface IEnvironmentFacts
{
    bool IsWindows { get; }

    bool IsLinux { get; }

    bool IsMacOS { get; }

    Architecture OperatingSystemArchitecture { get; }

    Architecture ProcessArchitecture { get; }

    string OperatingSystemDescription { get; }
}

/// <summary>
/// Production facts. Host selection uses <see cref="OperatingSystemArchitecture"/>,
/// so a macOS process running under Rosetta still selects the aarch64 HVF profile.
/// </summary>
public sealed class RuntimeEnvironmentFacts : IEnvironmentFacts
{
    public static RuntimeEnvironmentFacts Instance { get; } = new();

    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public Architecture OperatingSystemArchitecture => RuntimeInformation.OSArchitecture;

    public Architecture ProcessArchitecture => RuntimeInformation.ProcessArchitecture;

    public string OperatingSystemDescription => RuntimeInformation.OSDescription;
}

public sealed class EnvironmentDetector
{
    private readonly IEnvironmentFacts _facts;

    public EnvironmentDetector()
        : this(RuntimeEnvironmentFacts.Instance)
    {
    }

    public EnvironmentDetector(IEnvironmentFacts facts)
    {
        _facts = facts ?? throw new ArgumentNullException(nameof(facts));
    }

    public HostEnvironmentSnapshot Detect()
    {
        var platform = Classify(
            _facts.IsWindows,
            _facts.IsLinux,
            _facts.IsMacOS,
            _facts.OperatingSystemArchitecture);

        return new HostEnvironmentSnapshot
        {
            Platform = platform,
            OperatingSystemDescription = _facts.OperatingSystemDescription,
            OperatingSystemArchitecture = _facts.OperatingSystemArchitecture,
            ProcessArchitecture = _facts.ProcessArchitecture,
        };
    }

    public static HostPlatformKind Classify(
        bool isWindows,
        bool isLinux,
        bool isMacOS,
        Architecture operatingSystemArchitecture)
    {
        // OSArchitecture, not the process architecture, decides the guest ABI.
        if (isWindows && operatingSystemArchitecture == Architecture.X64)
            return HostPlatformKind.WindowsX64;

        if (isLinux && operatingSystemArchitecture == Architecture.X64)
            return HostPlatformKind.LinuxX64;

        if (isMacOS && operatingSystemArchitecture == Architecture.Arm64)
            return HostPlatformKind.MacOsArm64;

        return HostPlatformKind.Unsupported;
    }
}
