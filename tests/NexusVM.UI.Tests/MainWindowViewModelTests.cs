using System.Runtime.InteropServices;
using NexusVM.UI.ViewModels;
using NexusVM.Core.Acceleration;
using NexusVM.Core.Detectors;
using NexusVM.Core.Engine;
using NexusVM.Core.Models;

namespace NexusVM.UI.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Initialize_describes_an_apple_silicon_host_and_requires_firmware()
    {
        var viewModel = Create(HostPlatformKind.MacOsArm64, available: true);

        viewModel.Initialize();

        Assert.Equal("macOS Apple Silicon", viewModel.HostSummary);
        Assert.True(viewModel.FirmwareRequired);
        Assert.True(viewModel.AcceleratorReady);
        Assert.Equal(SurfaceEmbedStrategy.Framebuffer, viewModel.SurfaceStrategy);
        Assert.Null(viewModel.StartupDiagnostic);
        Assert.Contains("framebuffer", viewModel.SurfaceCaption, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Initialize_surfaces_a_kvm_permission_diagnostic()
    {
        var viewModel = Create(HostPlatformKind.LinuxX64, available: false);
        var inspections = 0;
        viewModel.InspectionCompleted += (_, _) => inspections++;

        viewModel.Initialize();

        Assert.Equal(1, inspections);
        Assert.NotNull(viewModel.StartupDiagnostic);
        Assert.Equal("KVM permission denied", viewModel.StartupDiagnostic!.Title);
        Assert.Contains("/dev/kvm", viewModel.StartupDiagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("usermod -aG kvm", viewModel.Remediation, StringComparison.Ordinal);
        Assert.True(viewModel.ShowRemediation);
        Assert.Equal(SurfaceEmbedStrategy.X11Reparent, viewModel.SurfaceStrategy);
    }

    [Fact]
    public void Launch_without_an_image_raises_the_diagnostic_instead_of_a_process()
    {
        var viewModel = Create(HostPlatformKind.WindowsX64, available: true);
        viewModel.Initialize();
        AccelerationDiagnosticException? diagnostic = null;
        PreparedLaunch? launch = null;
        viewModel.DiagnosticRaised += (_, exception) => diagnostic = exception;
        viewModel.LaunchRequested += (_, prepared) => launch = prepared;

        viewModel.LaunchCommand.Execute(null);

        Assert.Null(launch);
        Assert.NotNull(diagnostic);
        Assert.Equal("System image is required", diagnostic!.Title);
        Assert.False(viewModel.IsRunning);
    }

    [Fact]
    public void Launch_requests_the_whpx_contract()
    {
        var viewModel = Create(HostPlatformKind.WindowsX64, available: true);
        viewModel.Initialize();
        viewModel.SystemImagePath = Path.Combine(Path.GetTempPath(), "system.img");
        PreparedLaunch? launch = null;
        viewModel.LaunchRequested += (_, prepared) => launch = prepared;

        viewModel.LaunchCommand.Execute(null);

        Assert.NotNull(launch);
        Assert.Equal(new[] { "-accel", "whpx", "-m", "4G", "-smp", "4" }, launch!.Plan.Arguments.Take(6).ToArray());
        Assert.True(viewModel.IsRunning);
        Assert.Contains("-accel whpx -m 4G -smp 4", viewModel.CommandPreview, StringComparison.Ordinal);
    }

    [Fact]
    public void Launch_on_apple_silicon_requests_hvf_and_a_separate_firmware_argument()
    {
        var viewModel = Create(HostPlatformKind.MacOsArm64, available: true);
        viewModel.Initialize();
        var firmware = Path.Combine(Path.GetTempPath(), "QEMU_EFI.fd");
        viewModel.SystemImagePath = Path.Combine(Path.GetTempPath(), "gsi.img");
        viewModel.FirmwarePath = firmware;
        PreparedLaunch? launch = null;
        viewModel.LaunchRequested += (_, prepared) => launch = prepared;

        viewModel.LaunchCommand.Execute(null);

        Assert.NotNull(launch);
        Assert.Equal("qemu-system-aarch64", launch!.Plan.Binary);
        Assert.Equal(
            new[] { "-accel", "hvf", "-cpu", "host", "-m", "4G", "-smp", "4" },
            launch.Plan.Arguments.Take(8).ToArray());
        Assert.Contains(Path.GetFullPath(firmware), launch.Plan.Arguments);
        Assert.Equal(DisplayEmbedMode.Framebuffer, launch.Plan.DisplayMode);
    }

    private static MainWindowViewModel Create(HostPlatformKind platform, bool available)
    {
        var (windows, linux, mac, architecture, accelerator) = platform switch
        {
            HostPlatformKind.WindowsX64 => (true, false, false, Architecture.X64, "whpx"),
            HostPlatformKind.LinuxX64 => (false, true, false, Architecture.X64, "kvm"),
            HostPlatformKind.MacOsArm64 => (false, false, true, Architecture.Arm64, "hvf"),
            _ => throw new ArgumentOutOfRangeException(nameof(platform)),
        };

        var orchestrator = new HostOrchestrator(
            new EnvironmentDetector(new Facts(windows, linux, mac, architecture)),
            new ScriptedProbe(available, accelerator),
            new QemuCommandBuilder(_ => true, new FixedPort()),
            new StaticLocator());
        return new MainWindowViewModel(orchestrator);
    }

    private sealed class Facts : IEnvironmentFacts
    {
        public Facts(bool windows, bool linux, bool mac, Architecture architecture)
        {
            IsWindows = windows;
            IsLinux = linux;
            IsMacOS = mac;
            OperatingSystemArchitecture = architecture;
            ProcessArchitecture = architecture;
        }

        public bool IsWindows { get; }

        public bool IsLinux { get; }

        public bool IsMacOS { get; }

        public Architecture OperatingSystemArchitecture { get; }

        public Architecture ProcessArchitecture { get; }

        public string OperatingSystemDescription { get; } = "test";
    }

    private sealed class ScriptedProbe : IAccelerationProbe
    {
        private readonly bool _available;
        private readonly string _accelerator;

        public ScriptedProbe(bool available, string accelerator)
        {
            _available = available;
            _accelerator = accelerator;
        }

        public AccelerationProbeResult Probe(HostEnvironmentSnapshot environment)
        {
            if (_available)
                return AccelerationProbeResult.Available(_accelerator, _accelerator + " ready");

            return AccelerationProbeResult.Unavailable(
                _accelerator,
                "KVM permission denied",
                "The kernel device /dev/kvm exists, but this process cannot open it.",
                "sudo usermod -aG kvm $USER");
        }
    }

    private sealed class FixedPort : ILoopbackPortAllocator
    {
        private int _next = 4445;

        public int Allocate() => _next++;
    }

    private sealed class StaticLocator : IQemuBinaryLocator
    {
        public string? Locate(string binaryName) => "/usr/local/bin/" + binaryName;
    }
}
