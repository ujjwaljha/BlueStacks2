using System.Runtime.InteropServices;

namespace BlueStacks2.App.Embedding;

internal static class X11Native
{
    private const string Library = "libX11.so.6";
    private const int XaCardinal = 6;

    internal static bool IsLibraryAvailable() =>
        NativeLibrary.TryLoad(Library, out var handle) && Free(handle);

    internal static IntPtr OpenDisplay() => XOpenDisplay(IntPtr.Zero);

    internal static IntPtr CreateChild(IntPtr display, IntPtr parent)
    {
        var window = XCreateSimpleWindow(display, parent, 0, 0, 640, 480, 0, UIntPtr.Zero, UIntPtr.Zero);
        if (window == IntPtr.Zero)
            return IntPtr.Zero;

        _ = XMapWindow(display, window);
        _ = XFlush(display);
        return window;
    }

    internal static bool TryFindByProcessId(IntPtr display, int processId, out IntPtr window)
    {
        window = IntPtr.Zero;
        if (display == IntPtr.Zero || processId <= 0)
            return false;

        var root = XDefaultRootWindow(display);
        var atom = XInternAtom(display, "_NET_WM_PID", 0);
        if (root == IntPtr.Zero || atom == IntPtr.Zero)
            return false;

        var queue = new Queue<IntPtr>();
        queue.Enqueue(root);
        var seen = 0;
        while (queue.Count > 0 && seen < 5000)
        {
            var current = queue.Dequeue();
            seen++;
            if (current != root && ReadCardinal(display, current, atom) == processId)
            {
                window = current;
                return true;
            }

            if (XQueryTree(display, current, out _, out _, out var children, out var count) == 0 || children == IntPtr.Zero)
                continue;

            var limit = (int)Math.Min(count, 512);
            for (var index = 0; index < limit; index++)
                queue.Enqueue(Marshal.ReadIntPtr(children, index * IntPtr.Size));

            _ = XFree(children);
        }

        return false;
    }

    internal static void Reparent(IntPtr display, IntPtr guest, IntPtr parent, int width, int height)
    {
        _ = XReparentWindow(display, guest, parent, 0, 0);
        _ = XMapWindow(display, guest);
        Fit(display, guest, width, height);
        _ = XFlush(display);
    }

    internal static void Fit(IntPtr display, IntPtr guest, int width, int height)
    {
        if (display == IntPtr.Zero || guest == IntPtr.Zero)
            return;

        _ = XMoveResizeWindow(display, guest, 0, 0, (uint)Math.Max(1, width), (uint)Math.Max(1, height));
        _ = XFlush(display);
    }

    internal static void Destroy(IntPtr display, IntPtr window)
    {
        if (display == IntPtr.Zero)
            return;

        if (window != IntPtr.Zero)
        {
            _ = XDestroyWindow(display, window);
            _ = XFlush(display);
        }

        _ = XCloseDisplay(display);
    }

    private static bool Free(IntPtr handle)
    {
        NativeLibrary.Free(handle);
        return true;
    }

    private static int ReadCardinal(IntPtr display, IntPtr window, IntPtr atom)
    {
        var status = XGetWindowProperty(
            display,
            window,
            atom,
            0,
            1,
            0,
            new IntPtr(XaCardinal),
            out _,
            out var format,
            out var count,
            out _,
            out var property);

        if (status != 0 || property == IntPtr.Zero || count == UIntPtr.Zero)
        {
            if (property != IntPtr.Zero)
                _ = XFree(property);
            return -1;
        }

        try
        {
            if (format != 32)
                return -1;

            // X11 format 32 is stored in a C long, which is 64 bits on LP64.
            return IntPtr.Size == 8
                ? (int)Marshal.ReadInt64(property)
                : Marshal.ReadInt32(property);
        }
        finally
        {
            _ = XFree(property);
        }
    }

    [DllImport(Library)]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport(Library)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(Library)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(Library)]
    private static extern IntPtr XCreateSimpleWindow(
        IntPtr display,
        IntPtr parent,
        int x,
        int y,
        uint width,
        uint height,
        uint borderWidth,
        UIntPtr border,
        UIntPtr background);

    [DllImport(Library)]
    private static extern int XDestroyWindow(IntPtr display, IntPtr window);

    [DllImport(Library)]
    private static extern int XMapWindow(IntPtr display, IntPtr window);

    [DllImport(Library)]
    private static extern int XFlush(IntPtr display);

    [DllImport(Library)]
    private static extern IntPtr XInternAtom(IntPtr display, string name, int onlyIfExists);

    [DllImport(Library)]
    private static extern int XGetWindowProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        long longOffset,
        long longLength,
        int delete,
        IntPtr requiredType,
        out IntPtr actualType,
        out int actualFormat,
        out UIntPtr itemCount,
        out UIntPtr bytesAfter,
        out IntPtr propertyData);

    [DllImport(Library)]
    private static extern int XQueryTree(
        IntPtr display,
        IntPtr window,
        out IntPtr root,
        out IntPtr parent,
        out IntPtr children,
        out uint childCount);

    [DllImport(Library)]
    private static extern int XFree(IntPtr data);

    [DllImport(Library)]
    private static extern int XReparentWindow(IntPtr display, IntPtr window, IntPtr parent, int x, int y);

    [DllImport(Library)]
    private static extern int XMoveResizeWindow(IntPtr display, IntPtr window, int x, int y, uint width, uint height);
}

internal sealed class X11GuestWindowEmbedder
{
    private readonly IntPtr _display;
    private IntPtr _guest;

    public X11GuestWindowEmbedder(IntPtr display)
    {
        if (display == IntPtr.Zero)
            throw new ArgumentException("An X11 display is required.", nameof(display));

        _display = display;
    }

    public bool TryAttach(int processId, IntPtr parent, int width, int height)
    {
        if (parent == IntPtr.Zero)
            return false;

        if (!X11Native.TryFindByProcessId(_display, processId, out var guest))
            return false;

        X11Native.Reparent(_display, guest, parent, width, height);
        _guest = guest;
        return true;
    }

    public void Fit(int width, int height)
    {
        if (_guest != IntPtr.Zero)
            X11Native.Fit(_display, _guest, width, height);
    }
}
