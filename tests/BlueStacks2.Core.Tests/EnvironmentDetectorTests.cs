using System.Runtime.InteropServices;
using BlueStacks2.Core.Detection;
using BlueStacks2.Core.Models;

namespace BlueStacks2.Core.Tests;

public sealed class EnvironmentDetectorTests
{
    [Theory]
    [InlineData(true, false, false, Architecture.X64, HostPlatformKind.WindowsX64)]
    [InlineData(false, true, false, Architecture.X64, HostPlatformKind.LinuxX64)]
    [InlineData(false, false, true, Architecture.Arm64, HostPlatformKind.MacOsArm64)]
    [InlineData(true, false, false, Architecture.Arm64, HostPlatformKind.Unsupported)]
    [InlineData(false, true, false, Architecture.Arm64, HostPlatformKind.Unsupported)]
    [InlineData(false, false, true, Architecture.X64, HostPlatformKind.Unsupported)]
    [InlineData(false, false, false, Architecture.X64, HostPlatformKind.Unsupported)]
    public void Classify_selects_only_the_three_supported_hosts(
        bool windows,
        bool linux,
        bool mac,
        Architecture architecture,
        HostPlatformKind expected)
    {
        var kind = EnvironmentDetector.Classify(windows, linux, mac, architecture);
        Assert.Equal(expected, kind);
    }

    [Fact]
    public void Detect_uses_operating_system_architecture_when_the_process_is_translated()
    {
        var detector = new EnvironmentDetector(new FakeFacts
        {
            IsMacOS = true,
            OperatingSystemArchitecture = Architecture.Arm64,
            ProcessArchitecture = Architecture.X64,
            OperatingSystemDescription = "macOS",
        });

        var snapshot = detector.Detect();

        Assert.Equal(HostPlatformKind.MacOsArm64, snapshot.Platform);
        Assert.Equal(Architecture.X64, snapshot.ProcessArchitecture);
        Assert.Equal("macOS Apple Silicon", snapshot.Describe());
    }

    [Fact]
    public void Detect_matches_runtime_information()
    {
        var snapshot = new EnvironmentDetector().Detect();
        var expected = EnvironmentDetector.Classify(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
            RuntimeInformation.OSArchitecture);

        Assert.Equal(expected, snapshot.Platform);
        Assert.Equal(RuntimeInformation.OSArchitecture, snapshot.OperatingSystemArchitecture);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.OperatingSystemDescription));
    }

    private sealed class FakeFacts : IEnvironmentFacts
    {
        public bool IsWindows { get; init; }

        public bool IsLinux { get; init; }

        public bool IsMacOS { get; init; }

        public Architecture OperatingSystemArchitecture { get; init; }

        public Architecture ProcessArchitecture { get; init; }

        public string OperatingSystemDescription { get; init; } = "test";
    }
}
