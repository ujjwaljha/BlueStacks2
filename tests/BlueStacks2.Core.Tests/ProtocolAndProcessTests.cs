using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using BlueStacks2.Core.Acceleration;
using BlueStacks2.Core.Detection;
using BlueStacks2.Core.Launch;
using BlueStacks2.Core.Models;
using BlueStacks2.Core.Protocol;

namespace BlueStacks2.Core.Tests;

public sealed class ProtocolAndProcessTests
{
    [Fact]
    public void Qmp_reader_ignores_braces_inside_strings()
    {
        var buffer = "noise {\"a\":\"}\",\"b\":1} trailing";
        Assert.True(QmpMessageReader.TryTake(ref buffer, out var json));
        Assert.Equal("{\"a\":\"}\",\"b\":1}", json);
        Assert.Equal(" trailing", buffer);
    }

    [Fact]
    public void Qmp_reader_keeps_an_incomplete_object()
    {
        var buffer = "{\"execute\":\"qmp_capabilities\"";
        Assert.False(QmpMessageReader.TryTake(ref buffer, out _));
        Assert.Equal("{\"execute\":\"qmp_capabilities\"", buffer);
    }

    [Fact]
    public void Ppm_parser_converts_rgb_to_opaque_bgra()
    {
        var ppm = Encoding.ASCII.GetBytes("P6\n# comment\n2 1\n255\n").Concat(new byte[]
        {
            10, 20, 30,
            1, 2, 3,
        }).ToArray();

        Assert.True(PpmImageReader.TryParse(ppm, out var frame));
        Assert.NotNull(frame);
        Assert.Equal(2, frame!.Width);
        Assert.Equal(1, frame.Height);
        Assert.Equal(new byte[] { 30, 20, 10, 255, 3, 2, 1, 255 }, frame.Bgra32);
    }

    [Fact]
    public void Ppm_parser_rejects_a_short_raster()
    {
        var ppm = Encoding.ASCII.GetBytes("P6\n1 1\n255\n\xff");
        Assert.False(PpmImageReader.TryParse(ppm, out _));
    }

    [Fact]
    public async Task Pump_reads_one_screendump_frame()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var directory = Directory.CreateTempSubdirectory("bluestacks2-qmp").FullName;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ppm = Encoding.ASCII.GetBytes("P6\n1 1\n255\n").Concat(new byte[] { 9, 8, 7 }).ToArray();
        var server = ServeOneFrameAsync(listener, ppm, cts.Token);
        var pump = new QmpFramePump(TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(2));
        PixelFrame? seen = null;
        pump.FrameArrived += (_, frame) =>
        {
            seen = frame;
            cts.Cancel();
        };

        try
        {
            await pump.RunAsync(port, directory, cts.Token);
            await server;
        }
        finally
        {
            listener.Stop();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception)
            {
                // The pump may still be closing the frame file.
            }
        }

        Assert.NotNull(seen);
        Assert.Equal(1, seen!.Width);
        Assert.Equal(255, seen.Bgra32[3]);
        Assert.Equal(7, seen.Bgra32[0]);
    }

    [Fact]
    public void Locator_only_returns_the_two_qemu_system_binaries()
    {
        var directory = Directory.CreateTempSubdirectory("bluestacks2-qemu").FullName;
        try
        {
            var binary = Path.Combine(directory, "qemu-system-x86_64");
            File.WriteAllBytes(binary, new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(directory, "not-qemu"), new byte[] { 0 });
            var locator = new PathQemuBinaryLocator(directory);

            Assert.Equal(Path.GetFullPath(binary), locator.Locate("qemu-system-x86_64"));
            Assert.Null(locator.Locate("qemu-system-aarch64"));
            Assert.Throws<ArgumentOutOfRangeException>(() => locator.Locate("bash"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Prepare_does_not_look_for_qemu_when_acceleration_is_unavailable()
    {
        var orchestrator = new HostOrchestrator(
            new EnvironmentDetector(new HostFacts(linux: true, architecture: Architecture.X64)),
            new DeniedProbe(),
            new QemuCommandBuilder(_ => true, new FixedPort()),
            new ThrowingLocator());

        var exception = Assert.Throws<AccelerationDiagnosticException>(() =>
            orchestrator.Prepare(new GuestLaunchRequest { SystemImagePath = "/images/system.img" }));

        Assert.Equal("KVM permission denied", exception.Title);
    }

    [Fact]
    public void Prepare_reports_a_missing_qemu_binary()
    {
        var orchestrator = new HostOrchestrator(
            new EnvironmentDetector(new HostFacts(linux: true, architecture: Architecture.X64)),
            new ReadyProbe("kvm"),
            new QemuCommandBuilder(_ => true, new FixedPort()),
            new MissingLocator());

        var exception = Assert.Throws<AccelerationDiagnosticException>(() =>
            orchestrator.Prepare(new GuestLaunchRequest { SystemImagePath = "/images/system.img" }));

        Assert.Equal("QEMU was not found", exception.Title);
        Assert.Contains("qemu-system-x86_64", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_passes_each_flag_as_its_own_argument()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var directory = Directory.CreateTempSubdirectory("bluestacks2-exec").FullName;
        var argsFile = Path.Combine(directory, "args.txt");
        var binary = Path.Combine(directory, "qemu-system-x86_64");
        var script = "#!/bin/sh\nprintf '%s\\n' \"$@\" > '" + argsFile.Replace("'", "'\\''", StringComparison.Ordinal) + "'\n";
        await File.WriteAllTextAsync(binary, script);
        File.SetUnixFileMode(
            binary,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var plan = new QemuCommandBuilder(_ => true, new FixedPort()).Build(
            new EnvironmentDetector(new HostFacts(linux: false, architecture: Architecture.X64, windows: true)).Detect(),
            AccelerationProbeResult.Available("whpx", "ready"),
            new GuestLaunchRequest { SystemImagePath = "/images/system.img" });

        QemuGuestProcess? guest = null;
        try
        {
            guest = QemuGuestProcess.Start(binary, plan, directory);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!guest.HasExited && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.True(guest.HasExited);
            int? exitCode = null;
            guest.Exited += (_, code) => exitCode = code;
            Assert.Equal(0, exitCode);
            var lines = await File.ReadAllLinesAsync(argsFile);
            Assert.Equal("-accel", lines[0]);
            Assert.Equal("whpx", lines[1]);
            Assert.Equal("-m", lines[2]);
            Assert.Equal("4G", lines[3]);
            Assert.Equal("-smp", lines[4]);
            Assert.Equal("4", lines[5]);
            Assert.Contains(lines, line => line.StartsWith("tcp:127.0.0.1:", StringComparison.Ordinal));
        }
        finally
        {
            if (guest is not null)
                await guest.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task ServeOneFrameAsync(TcpListener listener, byte[] ppm, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"QMP\":{\"version\":{\"package\":\"test\"}}}\n"), cancellationToken);

        try
        {
            var buffer = string.Empty;
            var bytes = new byte[2048];
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!QmpMessageReader.TryTake(ref buffer, out var json))
                {
                    var read = await stream.ReadAsync(bytes, cancellationToken);
                    if (read == 0)
                        return;

                    buffer += Encoding.UTF8.GetString(bytes, 0, read);
                    continue;
                }

                using var document = JsonDocument.Parse(json);
                var execute = document.RootElement.GetProperty("execute").GetString();
                if (execute == "screendump")
                {
                    var file = document.RootElement.GetProperty("arguments").GetProperty("filename").GetString();
                    await File.WriteAllBytesAsync(file!, ppm, cancellationToken);
                }

                await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"return\":{}}\n"), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
        {
            // The pump closes the socket after the first frame.
        }
    }

    private sealed class FixedPort : ILoopbackPortAllocator
    {
        public int Allocate() => 4445;
    }

    private sealed class DeniedProbe : IAccelerationProbe
    {
        public AccelerationProbeResult Probe(HostEnvironmentSnapshot environment) =>
            AccelerationProbeResult.Unavailable("kvm", "KVM permission denied", "cannot open /dev/kvm", "usermod -aG kvm $USER");
    }

    private sealed class ReadyProbe : IAccelerationProbe
    {
        private readonly string _name;

        public ReadyProbe(string name) => _name = name;

        public AccelerationProbeResult Probe(HostEnvironmentSnapshot environment) =>
            AccelerationProbeResult.Available(_name, "ready");
    }

    private sealed class ThrowingLocator : IQemuBinaryLocator
    {
        public string? Locate(string binaryName) => throw new InvalidOperationException("locator should not be called");
    }

    private sealed class MissingLocator : IQemuBinaryLocator
    {
        public string? Locate(string binaryName) => null;
    }

    private sealed class HostFacts : IEnvironmentFacts
    {
        public HostFacts(bool linux, Architecture architecture, bool windows = false)
        {
            IsLinux = linux;
            IsWindows = windows;
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
