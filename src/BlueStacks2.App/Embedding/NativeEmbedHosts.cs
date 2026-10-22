using Avalonia.Controls;
using Avalonia.Platform;

namespace BlueStacks2.App.Embedding;

internal sealed class Win32EmbedHost : NativeControlHost
{
    public IntPtr Handle { get; private set; }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("HWND reparenting is only created on Windows.");

        if (!string.Equals(parent.HandleDescriptor, "HWND", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The Avalonia parent handle is '{parent.HandleDescriptor}', not an HWND.");
        }

        Handle = Win32Native.CreateChildWindow(parent.Handle);
        if (Handle == IntPtr.Zero)
            throw new InvalidOperationException("CreateWindowEx failed: " + MarshalLastError());

        return new PlatformHandle(Handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        Win32Native.Destroy(control.Handle);
        Handle = IntPtr.Zero;
        base.DestroyNativeControlCore(control);
    }

    private static int MarshalLastError() => System.Runtime.InteropServices.Marshal.GetLastWin32Error();
}

internal sealed class X11EmbedHost : NativeControlHost
{
    public IntPtr Display { get; private set; }

    public IntPtr WindowHandle { get; private set; }

    public static bool TryCheckAvailability(out string reason)
    {
        if (!OperatingSystem.IsLinux())
        {
            reason = "X11 reparenting is only used on Linux.";
            return false;
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            reason = "DISPLAY is not set. Native reparent requires an X11 server. Wayland cannot adopt the QEMU SDL window.";
            return false;
        }

        if (!X11Native.IsLibraryAvailable())
        {
            reason = "libX11.so.6 could not be loaded. Install the distro libx11 package.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!string.Equals(parent.HandleDescriptor, "XID", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The Avalonia parent handle is '{parent.HandleDescriptor}', not an X11 XID. Start the shell on the X11 backend.");
        }

        Display = X11Native.OpenDisplay();
        if (Display == IntPtr.Zero)
            throw new InvalidOperationException("XOpenDisplay failed. An X11 display is required to embed the QEMU window.");

        WindowHandle = X11Native.CreateChild(Display, parent.Handle);
        if (WindowHandle == IntPtr.Zero)
        {
            X11Native.Destroy(Display, IntPtr.Zero);
            Display = IntPtr.Zero;
            throw new InvalidOperationException("XCreateSimpleWindow failed.");
        }

        return new PlatformHandle(WindowHandle, "XID");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        X11Native.Destroy(Display, control.Handle);
        Display = IntPtr.Zero;
        WindowHandle = IntPtr.Zero;
        base.DestroyNativeControlCore(control);
    }
}
