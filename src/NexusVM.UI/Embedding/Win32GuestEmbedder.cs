using System.Runtime.InteropServices;
using System.Text;

namespace NexusVM.UI.Embedding;

internal static class Win32Native
{
    private const int GwlStyle = -16;
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const int SwShow = 5;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    internal static IntPtr CreateChildWindow(IntPtr parent)
    {
        return CreateWindowEx(
            0,
            "static",
            string.Empty,
            WsChild | WsVisible,
            0,
            0,
            64,
            64,
            parent,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    internal static bool TryFindGuestWindow(int processId, string titleFragment, out IntPtr hwnd)
    {
        var found = IntPtr.Zero;
        EnumWindows((candidate, lParam) =>
        {
            _ = GetWindowThreadProcessId(candidate, out var windowProcess);
            if (windowProcess != (uint)processId)
                return true;

            var title = new StringBuilder(512);
            _ = GetWindowText(candidate, title, title.Capacity);
            if (title.ToString().Contains(titleFragment, StringComparison.OrdinalIgnoreCase))
            {
                found = candidate;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        hwnd = found;
        return found != IntPtr.Zero;
    }

    internal static void Reparent(IntPtr guest, IntPtr parent)
    {
        _ = SetParent(guest, parent);
        var style = unchecked((int)GetWindowLongPtr(guest, GwlStyle).ToInt64());
        style &= ~WsPopup;
        style &= ~WsCaption;
        style &= ~WsThickFrame;
        style |= WsChild;
        style |= WsVisible;
        _ = SetWindowLongPtr(guest, GwlStyle, new IntPtr(style));
        _ = ShowWindow(guest, SwShow);
        FitGuest(guest, parent);
        _ = SetWindowPos(guest, IntPtr.Zero, 0, 0, 0, 0, SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        FitGuest(guest, parent);
    }

    internal static void FitGuest(IntPtr guest, IntPtr parent)
    {
        if (guest == IntPtr.Zero || parent == IntPtr.Zero)
            return;

        if (!GetClientRect(parent, out var rect))
            return;

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        _ = MoveWindow(guest, 0, 0, width, height, true);
    }

    internal static void Destroy(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
            _ = DestroyWindow(hwnd);
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int exStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newValue);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}

internal sealed class Win32GuestWindowEmbedder
{
    private IntPtr _guest;

    public bool TryAttach(int processId, IntPtr parent, string windowTitle)
    {
        if (parent == IntPtr.Zero || processId <= 0 || string.IsNullOrWhiteSpace(windowTitle))
            return false;

        if (!Win32Native.TryFindGuestWindow(processId, windowTitle, out var guest))
            return false;

        Win32Native.Reparent(guest, parent);
        _guest = guest;
        return true;
    }

    public void Fit(IntPtr parent)
    {
        if (_guest != IntPtr.Zero)
            Win32Native.FitGuest(_guest, parent);
    }
}
