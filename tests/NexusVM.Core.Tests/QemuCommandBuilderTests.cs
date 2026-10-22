using System.Runtime.InteropServices;
using NexusVM.Core.Detectors;
using NexusVM.Core.Engine;
using NexusVM.Core.Models;

namespace NexusVM.Core.Tests;

public sealed class QemuCommandBuilderTests
{
    [Fact]
    public void Windows_contract_is_whpx_with_four_gigabytes_and_four_cpus()
    {
        var plan = Build(HostPlatformKind.WindowsX64, system: Image("system.img"));

        Assert.Equal(HostProfiles.X86Binary, plan.Binary);
        Assert.Equal(new[] { "-accel", "whpx", "-m", "4G", "-smp", "4" }, plan.Arguments.Take(6).ToArray());
        Assert.Contains("-accel whpx -m 4G -smp 4", plan.DisplayCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("-cpu", plan.Arguments);
        Assert.DoesNotContain("kvm", plan.Arguments);
        Assert.DoesNotContain("hvf", plan.Arguments);
        Assert.Equal(DisplayEmbedMode.NativeWindow, plan.DisplayMode);
        Assert.Contains("sdl,window-close=off", plan.Arguments);
        Assert.Contains("tcp:127.0.0.1:4445,server,nowait", plan.Arguments);
        Assert.Contains("user,id=net0,hostfwd=tcp:127.0.0.1:4446-:5555", plan.Arguments);
        Assert.Equal(4446, plan.AdbHostPort);
        Assert.DoesNotContain(plan.Arguments, argument => argument.Contains("0.0.0.0", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Arguments, argument => argument.Contains("-accel ", StringComparison.Ordinal));
    }

    [Fact]
    public void Linux_contract_is_kvm_with_four_gigabytes_and_four_cpus()
    {
        var plan = Build(HostPlatformKind.LinuxX64, system: Image("system.img"));

        Assert.Equal(HostProfiles.X86Binary, plan.Binary);
        Assert.Equal(new[] { "-accel", "kvm", "-m", "4G", "-smp", "4" }, plan.Arguments.Take(6).ToArray());
        Assert.Contains("-accel kvm -m 4G -smp 4", plan.DisplayCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("whpx", plan.Arguments);
        Assert.DoesNotContain("hvf", plan.Arguments);
        Assert.Contains("q35", plan.Arguments);
    }

    [Fact]
    public void Mac_contract_is_hvf_host_cpu_and_places_firmware_in_its_own_argument()
    {
        var firmware = Image("QEMU EFI.fd");
        var plan = Build(HostPlatformKind.MacOsArm64, system: Image("system.img"), firmware: firmware);

        Assert.Equal(HostProfiles.Aarch64Binary, plan.Binary);
        Assert.Equal(
            new[] { "-accel", "hvf", "-cpu", "host", "-m", "4G", "-smp", "4" },
            plan.Arguments.Take(8).ToArray());
        Assert.Contains("-accel hvf -cpu host -m 4G -smp 4", plan.DisplayCommand, StringComparison.Ordinal);
        Assert.Equal(DisplayEmbedMode.Framebuffer, plan.DisplayMode);
        Assert.Contains("none", plan.Arguments);
        Assert.Contains(Path.GetFullPath(firmware), plan.Arguments);
        Assert.DoesNotContain("kvm", plan.Arguments);
        Assert.DoesNotContain("whpx", plan.Arguments);
        Assert.Contains("virt,gic-version=3", plan.Arguments);
    }

    [Fact]
    public void Drive_paths_escape_commas_and_keep_spaces_inside_one_argument()
    {
        var system = Path.Combine(Path.GetTempPath(), "system,1.img");
        var plan = Build(HostPlatformKind.LinuxX64, system: system);
        var full = Path.GetFullPath(system);
        var drive = Assert.Single(plan.Arguments, argument => argument.Contains("id=system", StringComparison.Ordinal));

        Assert.Contains("file=" + full.Replace(",", ",,", StringComparison.Ordinal), drive, StringComparison.Ordinal);
        Assert.DoesNotContain("file=" + full, drive, StringComparison.Ordinal);
        Assert.Contains("format=raw", drive, StringComparison.Ordinal);
    }

    [Fact]
    public void Qcow2_extension_selects_the_qcow2_format()
    {
        var plan = Build(HostPlatformKind.WindowsX64, system: Image("system.qcow2"));
        var drive = Assert.Single(plan.Arguments, argument => argument.Contains("id=system", StringComparison.Ordinal));
        Assert.Contains("format=qcow2", drive, StringComparison.Ordinal);
    }

    [Fact]
    public void Optional_data_image_adds_a_second_drive()
    {
        var plan = Build(HostPlatformKind.LinuxX64, system: Image("system.img"), data: Image("userdata.img"));
        Assert.Contains(plan.Arguments, argument => argument.Contains("id=userdata", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_system_image_is_a_diagnostic()
    {
        var exception = Assert.Throws<AccelerationDiagnosticException>(() =>
            Build(HostPlatformKind.LinuxX64, system: Image("missing.img"), exists: _ => false));

        Assert.Equal("System image was not found", exception.Title);
    }

    [Fact]
    public void Control_characters_in_a_path_are_rejected()
    {
        var exception = Assert.Throws<AccelerationDiagnosticException>(() =>
            Build(HostPlatformKind.LinuxX64, system: Image("system.img") + "\n"));

        Assert.Contains("not safe", exception.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mac_without_firmware_is_a_diagnostic()
    {
        var exception = Assert.Throws<AccelerationDiagnosticException>(() =>
            Build(HostPlatformKind.MacOsArm64, system: Image("system.img"), firmware: null));

        Assert.Equal("UEFI firmware is required", exception.Title);
        Assert.Contains("QEMU_EFI.fd", exception.Remediation, StringComparison.Ordinal);
    }

    [Fact]
    public void Unavailable_accelerator_is_thrown_before_a_command_is_built()
    {
        var exception = Assert.Throws<AccelerationDiagnosticException>(() =>
            new QemuCommandBuilder(_ => true, new FixedPort()).Build(
                Snapshot(HostPlatformKind.LinuxX64),
                AccelerationProbeResult.Unavailable("kvm", "KVM permission denied", "cannot open /dev/kvm", "usermod -aG kvm $USER"),
                new GuestLaunchRequest { SystemImagePath = Image("system.img") }));

        Assert.Equal("KVM permission denied", exception.Title);
    }

    [Fact]
    public void Mismatched_accelerator_name_is_rejected()
    {
        var exception = Assert.Throws<AccelerationDiagnosticException>(() =>
            new QemuCommandBuilder(_ => true, new FixedPort()).Build(
                Snapshot(HostPlatformKind.LinuxX64),
                AccelerationProbeResult.Available("whpx", "wrong host"),
                new GuestLaunchRequest { SystemImagePath = Image("system.img") }));

        Assert.Equal("Acceleration probe does not match the host", exception.Title);
    }

    [Fact]
    public void Unsupported_platform_is_a_diagnostic()
    {
        var exception = Assert.Throws<AccelerationDiagnosticException>(() =>
            new QemuCommandBuilder(_ => true, new FixedPort()).Build(
                Snapshot(HostPlatformKind.Unsupported),
                AccelerationProbeResult.Available("none", "no"),
                new GuestLaunchRequest { SystemImagePath = Image("system.img") }));

        Assert.Equal("Unsupported host configuration", exception.Title);
    }

    [Fact]
    public void Display_command_quotes_arguments_that_contain_spaces()
    {
        var plan = Build(HostPlatformKind.MacOsArm64, system: Image("my system.img"), firmware: Image("QEMU EFI.fd"));
        Assert.Contains("\"", plan.DisplayCommand, StringComparison.Ordinal);
        Assert.StartsWith("qemu-system-aarch64 ", plan.DisplayCommand, StringComparison.Ordinal);
    }

    private static QemuLaunchPlan Build(
        HostPlatformKind platform,
        string system,
        string? data = null,
        string? firmware = null,
        Func<string, bool>? exists = null)
    {
        var builder = new QemuCommandBuilder(exists ?? (_ => true), new FixedPort());
        return builder.Build(
            Snapshot(platform),
            AccelerationProbeResult.Available(HostProfiles.AcceleratorName(platform), "ready"),
            new GuestLaunchRequest
            {
                SystemImagePath = system,
                DataImagePath = data,
                FirmwarePath = firmware,
            });
    }

    private static HostEnvironmentSnapshot Snapshot(HostPlatformKind platform)
    {
        var (windows, linux, mac, architecture) = platform switch
        {
            HostPlatformKind.WindowsX64 => (true, false, false, Architecture.X64),
            HostPlatformKind.LinuxX64 => (false, true, false, Architecture.X64),
            HostPlatformKind.MacOsArm64 => (false, false, true, Architecture.Arm64),
            _ => (false, false, false, Architecture.X86),
        };

        return new EnvironmentDetector(new PlanFacts(windows, linux, mac, architecture)).Detect();
    }

    private static string Image(string name) => Path.Combine(Path.GetTempPath(), "nexusvm-tests", name);

    private sealed class FixedPort : ILoopbackPortAllocator
    {
        private int _next = 4445;

        public int Allocate() => _next++;
    }

    private sealed class PlanFacts : IEnvironmentFacts
    {
        public PlanFacts(bool windows, bool linux, bool mac, Architecture architecture)
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
}
