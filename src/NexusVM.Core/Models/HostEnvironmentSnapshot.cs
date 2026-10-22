using System.Runtime.InteropServices;

namespace NexusVM.Core.Models;

public sealed record HostEnvironmentSnapshot
{
    public required HostPlatformKind Platform { get; init; }

    public required string OperatingSystemDescription { get; init; }

    public required Architecture OperatingSystemArchitecture { get; init; }

    public required Architecture ProcessArchitecture { get; init; }

    public bool IsSupported => Platform != HostPlatformKind.Unsupported;

    public string Describe() => Platform switch
    {
        HostPlatformKind.WindowsX64 => "Windows x64",
        HostPlatformKind.LinuxX64 => "Linux x64",
        HostPlatformKind.MacOsArm64 => "macOS Apple Silicon",
        _ => $"Unsupported ({OperatingSystemDescription}, {OperatingSystemArchitecture})",
    };
}
