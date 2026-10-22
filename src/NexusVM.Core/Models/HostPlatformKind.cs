namespace NexusVM.Core.Models;

/// <summary>
/// The three host configurations this shell can launch, plus every other combination.
/// </summary>
public enum HostPlatformKind
{
    Unsupported = 0,
    WindowsX64 = 1,
    LinuxX64 = 2,
    MacOsArm64 = 3,
}

public enum DisplayEmbedMode
{
    None = 0,
    NativeWindow = 1,
    Framebuffer = 2,
}

public enum SurfaceEmbedStrategy
{
    None = 0,
    Win32Reparent = 1,
    X11Reparent = 2,
    Framebuffer = 3,
}

/// <summary>
/// Single map from a detected host to the QEMU binary, accelerator, and embed strategy.
/// </summary>
public static class HostProfiles
{
    public const string X86Binary = "qemu-system-x86_64";
    public const string Aarch64Binary = "qemu-system-aarch64";

    public static string AcceleratorName(HostPlatformKind kind) => kind switch
    {
        HostPlatformKind.WindowsX64 => "whpx",
        HostPlatformKind.LinuxX64 => "kvm",
        HostPlatformKind.MacOsArm64 => "hvf",
        _ => "none",
    };

    public static string QemuBinary(HostPlatformKind kind) => kind switch
    {
        HostPlatformKind.WindowsX64 or HostPlatformKind.LinuxX64 => X86Binary,
        HostPlatformKind.MacOsArm64 => Aarch64Binary,
        _ => string.Empty,
    };

    public static DisplayEmbedMode Display(HostPlatformKind kind) => kind switch
    {
        HostPlatformKind.WindowsX64 or HostPlatformKind.LinuxX64 => DisplayEmbedMode.NativeWindow,
        HostPlatformKind.MacOsArm64 => DisplayEmbedMode.Framebuffer,
        _ => DisplayEmbedMode.None,
    };

    public static SurfaceEmbedStrategy Surface(HostPlatformKind kind) => kind switch
    {
        HostPlatformKind.WindowsX64 => SurfaceEmbedStrategy.Win32Reparent,
        HostPlatformKind.LinuxX64 => SurfaceEmbedStrategy.X11Reparent,
        HostPlatformKind.MacOsArm64 => SurfaceEmbedStrategy.Framebuffer,
        _ => SurfaceEmbedStrategy.None,
    };
}
