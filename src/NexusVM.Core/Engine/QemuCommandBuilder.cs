using NexusVM.Core.Acceleration;
using NexusVM.Core.Models;

namespace NexusVM.Core.Engine;

public sealed class QemuCommandBuilder
{
    public const string GuestWindowTitle = "NexusVM";

    private readonly Func<string, bool> _fileExists;
    private readonly ILoopbackPortAllocator _ports;

    public QemuCommandBuilder(Func<string, bool>? fileExists = null, ILoopbackPortAllocator? ports = null)
    {
        _fileExists = fileExists ?? File.Exists;
        _ports = ports ?? new TcpLoopbackPortAllocator();
    }

    public QemuLaunchPlan Build(
        HostEnvironmentSnapshot environment,
        AccelerationProbeResult acceleration,
        GuestLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(acceleration);
        ArgumentNullException.ThrowIfNull(request);

        if (environment.Platform == HostPlatformKind.Unsupported)
            throw HostCompatibility.ForUnsupported(environment).ToException();

        if (!acceleration.IsAvailable)
            throw acceleration.ToException();

        var expectedAccelerator = HostProfiles.AcceleratorName(environment.Platform);
        if (!string.Equals(acceleration.AcceleratorName, expectedAccelerator, StringComparison.Ordinal))
        {
            throw new AccelerationDiagnosticException(
                "Acceleration probe does not match the host",
                $"The host is {environment.Describe()} and requires {expectedAccelerator}, but the probe reported {acceleration.AcceleratorName}.",
                "Run the host check again. The launcher will not emit an accelerator flag for a different operating system.",
                new[]
                {
                    "platform=" + environment.Platform,
                    "accelerator=" + acceleration.AcceleratorName,
                });
        }

        var system = RequireImage(request.SystemImagePath, "System image", required: true);
        var data = RequireImage(request.DataImagePath, "Data image", required: false);
        var firmwareRequired = environment.Platform == HostPlatformKind.MacOsArm64;
        var firmware = RequireImage(request.FirmwarePath, "UEFI firmware", firmwareRequired);

        var port = _ports.Allocate();
        if (port is < 1 or > 65535)
            throw new InvalidOperationException($"The loopback port allocator returned {port}.");

        var arguments = new List<string>();
        switch (environment.Platform)
        {
            case HostPlatformKind.WindowsX64:
                AddContract(arguments, "whpx", cpuHost: false);
                AddPcGuest(arguments, DisplayEmbedMode.NativeWindow, port, system!, data);
                break;
            case HostPlatformKind.LinuxX64:
                AddContract(arguments, "kvm", cpuHost: false);
                AddPcGuest(arguments, DisplayEmbedMode.NativeWindow, port, system!, data);
                break;
            case HostPlatformKind.MacOsArm64:
                AddContract(arguments, "hvf", cpuHost: true);
                AddArmGuest(arguments, port, system!, data, firmware!);
                break;
            default:
                throw HostCompatibility.ForUnsupported(environment).ToException();
        }

        var binary = HostProfiles.QemuBinary(environment.Platform);
        return new QemuLaunchPlan
        {
            Binary = binary,
            Arguments = arguments,
            DisplayCommand = QemuOptionSyntax.FormatCommand(binary, arguments),
            Platform = environment.Platform,
            DisplayMode = HostProfiles.Display(environment.Platform),
            QmpPort = port,
            WindowTitle = GuestWindowTitle,
        };
    }

    public string BuildCommandLine(
        HostEnvironmentSnapshot environment,
        AccelerationProbeResult acceleration,
        GuestLaunchRequest request) =>
        Build(environment, acceleration, request).DisplayCommand;

    private static void AddContract(List<string> arguments, string accelerator, bool cpuHost)
    {
        arguments.Add("-accel");
        arguments.Add(accelerator);
        if (cpuHost)
        {
            arguments.Add("-cpu");
            arguments.Add("host");
        }

        arguments.Add("-m");
        arguments.Add("4G");
        arguments.Add("-smp");
        arguments.Add("4");
    }

    private static void AddPcGuest(
        List<string> arguments,
        DisplayEmbedMode display,
        int qmpPort,
        string systemImage,
        string? dataImage)
    {
        arguments.Add("-nodefaults");
        arguments.Add("-machine");
        arguments.Add("q35");
        arguments.Add("-device");
        arguments.Add("virtio-vga");
        arguments.Add("-device");
        arguments.Add("qemu-xhci");
        arguments.Add("-device");
        arguments.Add("usb-tablet");
        AddDisplay(arguments, display);
        AddQmp(arguments, qmpPort);
        AddDrive(arguments, "system", systemImage);
        if (dataImage is not null)
            AddDrive(arguments, "userdata", dataImage);
        AddUserNet(arguments);
    }

    private static void AddArmGuest(
        List<string> arguments,
        int qmpPort,
        string systemImage,
        string? dataImage,
        string firmware)
    {
        arguments.Add("-nodefaults");
        arguments.Add("-machine");
        arguments.Add("virt,gic-version=3");
        arguments.Add("-bios");
        arguments.Add(firmware);
        arguments.Add("-device");
        arguments.Add("virtio-gpu-pci");
        arguments.Add("-device");
        arguments.Add("qemu-xhci");
        arguments.Add("-device");
        arguments.Add("usb-kbd");
        arguments.Add("-device");
        arguments.Add("usb-tablet");
        AddDisplay(arguments, DisplayEmbedMode.Framebuffer);
        AddQmp(arguments, qmpPort);
        AddDrive(arguments, "system", systemImage);
        if (dataImage is not null)
            AddDrive(arguments, "userdata", dataImage);
        AddUserNet(arguments);
    }

    private static void AddDisplay(List<string> arguments, DisplayEmbedMode display)
    {
        arguments.Add("-display");
        arguments.Add(display == DisplayEmbedMode.NativeWindow ? "sdl,window-close=off" : "none");
        arguments.Add("-name");
        arguments.Add(GuestWindowTitle);
    }

    private static void AddQmp(List<string> arguments, int port)
    {
        // QMP stays on loopback. The port is chosen by this process, not by the image path.
        arguments.Add("-qmp");
        arguments.Add("tcp:127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",server,nowait");
    }

    private static void AddDrive(List<string> arguments, string id, string path)
    {
        arguments.Add("-drive");
        arguments.Add(QemuOptionSyntax.Drive(id, QemuOptionSyntax.DiskFormat(path), path, readOnly: false));
        arguments.Add("-device");
        arguments.Add($"virtio-blk-pci,drive={id}");
    }

    private static void AddUserNet(List<string> arguments)
    {
        arguments.Add("-netdev");
        arguments.Add("user,id=net0");
        arguments.Add("-device");
        arguments.Add("virtio-net-pci,netdev=net0");
    }

    private string? RequireImage(string? path, string label, bool required)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (!required)
                return null;

            var remediation = label == "UEFI firmware"
                ? "Apple Silicon boots an arm64 GSI on the QEMU virt machine and needs an AAVMF firmware image such as QEMU_EFI.fd."
                : "Use the file picker, or type an absolute path to a raw or qcow2 image.";

            throw new AccelerationDiagnosticException(
                $"{label} is required",
                $"Choose a {label.ToLowerInvariant()} before launching the guest.",
                remediation);
        }

        if (path.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new AccelerationDiagnosticException(
                $"{label} path is not safe to pass to QEMU",
                "The path contains a newline or a null character.",
                "Choose a local file path without control characters.");
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new AccelerationDiagnosticException(
                $"{label} path is invalid",
                ex.Message,
                "Choose a local file path without control characters.",
                new[] { path },
                ex);
        }

        if (!_fileExists(full))
        {
            throw new AccelerationDiagnosticException(
                $"{label} was not found",
                $"The file does not exist: {full}",
                "Check the path and that the image has been downloaded or built.",
                new[] { full });
        }

        return full;
    }
}
