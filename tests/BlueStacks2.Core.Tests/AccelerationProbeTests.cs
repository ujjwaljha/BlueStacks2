using System.Runtime.InteropServices;
using BlueStacks2.Core.Acceleration;
using BlueStacks2.Core.Detection;
using BlueStacks2.Core.Launch;

namespace BlueStacks2.Core.Tests;

public sealed class AccelerationProbeTests
{
    [Fact]
    public void Linux_missing_device_names_dev_kvm_and_modprobe()
    {
        var probe = new LinuxKvmProbe(
            exists: _ => false,
            openReadWrite: _ => throw new InvalidOperationException("should not open"),
            readCpuInfo: () => "flags : vmx");

        var result = probe.Probe();

        Assert.False(result.IsAvailable);
        Assert.Equal("KVM device is missing", result.DiagnosticTitle);
        Assert.Contains("/dev/kvm", result.Summary, StringComparison.Ordinal);
        Assert.Contains("modprobe", result.Remediation, StringComparison.Ordinal);
        Assert.Contains("vmx", string.Join('\n', result.Details), StringComparison.Ordinal);
        var exception = result.ToException();
        Assert.Equal("KVM device is missing", exception.Title);
    }

    [Fact]
    public void Linux_permission_denial_tells_the_user_to_join_the_kvm_group()
    {
        var probe = new LinuxKvmProbe(
            exists: _ => true,
            openReadWrite: _ => throw new UnauthorizedAccessException("denied"),
            describeMode: _ => "UserRead, UserWrite, GroupRead, GroupWrite");

        var result = probe.Probe();

        Assert.False(result.IsAvailable);
        Assert.Equal("KVM permission denied", result.DiagnosticTitle);
        Assert.Contains("/dev/kvm", result.Summary, StringComparison.Ordinal);
        Assert.Contains("usermod -aG kvm", result.Remediation, StringComparison.Ordinal);
        Assert.Contains("unixMode=", string.Join('\n', result.Details), StringComparison.Ordinal);
    }

    [Fact]
    public void Linux_open_success_reports_kvm()
    {
        var probe = new LinuxKvmProbe(
            exists: _ => true,
            openReadWrite: _ => new MemoryStream());

        var result = probe.Probe();

        Assert.True(result.IsAvailable);
        Assert.Equal("kvm", result.AcceleratorName);
    }

    [Fact]
    public void Windows_missing_library_names_the_optional_feature()
    {
        var probe = new WindowsWhpxProbe(new FakeWhpx { LibraryPresent = false, FirmwareEnabled = false });

        var result = probe.Probe();

        Assert.False(result.IsAvailable);
        Assert.Equal("Windows Hypervisor Platform is disabled", result.DiagnosticTitle);
        Assert.Contains("WinHvPlatform.dll", result.Summary, StringComparison.Ordinal);
        Assert.Contains("HypervisorPlatform", result.Remediation, StringComparison.Ordinal);
        Assert.Contains("Windows Hypervisor Platform", result.Remediation, StringComparison.Ordinal);
        Assert.Contains("PF_VIRT_FIRMWARE_ENABLED", string.Join('\n', result.Details), StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_hypervisor_absent_asks_for_a_reboot()
    {
        var probe = new WindowsWhpxProbe(new FakeWhpx
        {
            LibraryPresent = true,
            QuerySucceeded = true,
            PresentFlag = 0,
            HResult = 0,
        });

        var result = probe.Probe();

        Assert.False(result.IsAvailable);
        Assert.Equal("Windows hypervisor is not running", result.DiagnosticTitle);
        Assert.Contains("bcdedit /set hypervisorlaunchtype auto", result.Remediation, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_hypervisor_present_is_ready()
    {
        var probe = new WindowsWhpxProbe(new FakeWhpx
        {
            LibraryPresent = true,
            QuerySucceeded = true,
            PresentFlag = 1,
            HResult = 0,
        });

        var result = probe.Probe();

        Assert.True(result.IsAvailable);
        Assert.Equal("whpx", result.AcceleratorName);
    }

    [Fact]
    public void Windows_hresult_is_included_in_the_diagnostic()
    {
        var probe = new WindowsWhpxProbe(new FakeWhpx
        {
            LibraryPresent = true,
            QuerySucceeded = true,
            HResult = unchecked((int)0x80004005),
        });

        var result = probe.Probe();

        Assert.False(result.IsAvailable);
        Assert.Contains("80004005", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mac_hv_support_one_is_ready()
    {
        var probe = new MacOsHvfProbe(new FakeSysctl("1", string.Empty, 0));
        var result = probe.Probe();

        Assert.True(result.IsAvailable);
        Assert.Equal("hvf", result.AcceleratorName);
        Assert.Contains("kern.hv_support", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Mac_hv_support_zero_is_a_diagnostic()
    {
        var probe = new MacOsHvfProbe(new FakeSysctl("0", string.Empty, 0));
        var result = probe.Probe();

        Assert.False(result.IsAvailable);
        Assert.Equal("Hypervisor.framework is unavailable", result.DiagnosticTitle);
        Assert.Contains("kern.hv_support", result.Summary, StringComparison.Ordinal);
        Assert.Contains("kern.hv_support", result.Remediation, StringComparison.Ordinal);
    }

    [Fact]
    public void Mac_sysctl_failure_is_a_diagnostic()
    {
        var probe = new MacOsHvfProbe(new FakeSysctl(string.Empty, "no such key", 1));
        var result = probe.Probe();

        Assert.False(result.IsAvailable);
        Assert.Contains("kern.hv_support", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_host_names_the_three_supported_configurations()
    {
        var snapshot = new EnvironmentDetector(new ScriptedFacts(windows: true, architecture: Architecture.Arm64)).Detect();
        var result = new RuntimeAccelerationProbe().Probe(snapshot);

        Assert.False(result.IsAvailable);
        Assert.Equal("Unsupported host configuration", result.DiagnosticTitle);
        Assert.Contains("WHPX", result.Summary, StringComparison.Ordinal);
        Assert.Contains("KVM", result.Summary, StringComparison.Ordinal);
        Assert.Contains("HVF", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_host_probe_does_not_throw()
    {
        var readiness = HostOrchestrator.CreateDefault().Inspect();

        Assert.False(string.IsNullOrWhiteSpace(readiness.Acceleration.Summary));
        Assert.False(string.IsNullOrWhiteSpace(readiness.Environment.Describe()));
    }

    private sealed class FakeWhpx : IWhpxCapabilitySource
    {
        public bool LibraryPresent { get; init; }

        public bool QuerySucceeded { get; init; }

        public int PresentFlag { get; init; }

        public int HResult { get; init; }

        public bool? FirmwareEnabled { get; init; }

        public bool IsHypervisorPlatformLibraryPresent => LibraryPresent;

        public bool? IsFirmwareVirtualizationEnabled => FirmwareEnabled;

        public bool TryQueryHypervisorPresent(out int presentFlag, out int hresult)
        {
            presentFlag = PresentFlag;
            hresult = HResult;
            return QuerySucceeded;
        }
    }

    private sealed class FakeSysctl : ISystemControl
    {
        private readonly string _stdout;
        private readonly string _stderr;
        private readonly int _exitCode;

        public FakeSysctl(string stdout, string stderr, int exitCode)
        {
            _stdout = stdout;
            _stderr = stderr;
            _exitCode = exitCode;
        }

        public bool TryRead(string key, out string stdout, out string stderr, out int exitCode)
        {
            Assert.Equal(MacOsHvfProbe.SupportKey, key);
            stdout = _stdout;
            stderr = _stderr;
            exitCode = _exitCode;
            return true;
        }
    }

    private sealed class ScriptedFacts : IEnvironmentFacts
    {
        public ScriptedFacts(bool windows, Architecture architecture)
        {
            IsWindows = windows;
            OperatingSystemArchitecture = architecture;
            ProcessArchitecture = architecture;
        }

        public bool IsWindows { get; }

        public bool IsLinux { get; }

        public bool IsMacOS { get; }

        public Architecture OperatingSystemArchitecture { get; }

        public Architecture ProcessArchitecture { get; }

        public string OperatingSystemDescription { get; } = "test-os";
    }
}
