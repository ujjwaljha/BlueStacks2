using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NexusVM.UI.Embedding;
using NexusVM.Core.Models;

namespace NexusVM.UI.Views;

/// <summary>
/// Host surface for the guest.
/// Windows x64 reparents the QEMU SDL HWND into a native child.
/// Linux x64 reparents that SDL window with XReparentWindow.
/// macOS Apple Silicon presents BGRA frames captured over loopback QMP, because
/// Cocoa will not adopt a foreign QEMU window.
/// </summary>
public partial class QemuSurfaceView : UserControl
{
    private const int MaxEmbedAttempts = 50;

    private Win32EmbedHost? _win32;
    private X11EmbedHost? _x11;
    private Win32GuestWindowEmbedder? _win32Embedder;
    private X11GuestWindowEmbedder? _x11Embedder;
    private DispatcherTimer? _timer;
    private WriteableBitmap? _bitmap;
    private SurfaceEmbedStrategy _strategy;
    private int _processId;
    private string _windowTitle = QemuCommandBuilderTitle();
    private int _attempts;
    private bool _attached;

    public QemuSurfaceView()
    {
        InitializeComponent();
    }

    public void ApplyStrategy(SurfaceEmbedStrategy strategy)
    {
        _strategy = strategy;
        StopTimer();
        _attached = false;
        _win32Embedder = null;
        _x11Embedder = null;
        NativeSlot.Content = null;
        FrameImage.IsVisible = false;
        FrameImage.Source = null;
        _bitmap = null;

        switch (strategy)
        {
            case SurfaceEmbedStrategy.Win32Reparent when OperatingSystem.IsWindows():
                _win32 ??= new Win32EmbedHost();
                NativeSlot.Content = _win32;
                ShowPlaceholder(
                    "Windows guest surface",
                    "After launch, the Android-x86_64 SDL window is found by process id and title, then reparented into this HWND with SetParent.");
                break;
            case SurfaceEmbedStrategy.X11Reparent when OperatingSystem.IsLinux():
                if (!X11EmbedHost.TryCheckAvailability(out var reason))
                {
                    ShowPlaceholder("X11 surface unavailable", reason);
                    break;
                }

                _x11 ??= new X11EmbedHost();
                NativeSlot.Content = _x11;
                ShowPlaceholder(
                    "Linux guest surface",
                    "After launch, the SDL window is located with _NET_WM_PID and reparented into this X11 child. The shell pins the X11 backend so Wayland is not asked to adopt a foreign toplevel.");
                break;
            case SurfaceEmbedStrategy.Framebuffer:
                FrameImage.IsVisible = true;
                ShowPlaceholder(
                    "HVF framebuffer",
                    "Apple Silicon boots an arm64 GSI with HVF. Cocoa cannot adopt the QEMU window, so this surface shows BGRA frames captured from the loopback QMP screendump channel.");
                break;
            default:
                ShowPlaceholder(
                    "No embed path for this host",
                    "Supported surfaces are a Win32 HWND on Windows x64, an X11 child on Linux x64, and a framebuffer on macOS Apple Silicon.");
                break;
        }
    }

    public void BeginNativeEmbed(int processId, string windowTitle)
    {
        _processId = processId;
        _windowTitle = windowTitle;
        _attempts = 0;
        _attached = false;
        if (_strategy is not (SurfaceEmbedStrategy.Win32Reparent or SurfaceEmbedStrategy.X11Reparent))
            return;

        ShowPlaceholder(
            "Waiting for the QEMU window",
            $"Looking for a guest window owned by process {processId} and titled \"{windowTitle}\".");
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick -= OnEmbedTick;
        _timer.Tick += OnEmbedTick;
        _timer.Start();
    }

    public void PresentFrame(PixelFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Width > PpmLimit() || frame.Height > PpmLimit())
            return;

        if (Dispatcher.UIThread.CheckAccess())
            ApplyFrame(frame);
        else
            Dispatcher.UIThread.Post(() => ApplyFrame(frame));
    }

    public void ShowFault(string title, string body)
    {
        if (Dispatcher.UIThread.CheckAccess())
            ShowPlaceholder(title, body);
        else
            Dispatcher.UIThread.Post(() => ShowPlaceholder(title, body));
    }

    public void ShowIdle()
    {
        StopTimer();
        _attached = false;
        ApplyStrategy(_strategy);
    }

    private void OnEmbedTick(object? sender, EventArgs e)
    {
        _attempts++;
        if (_attempts > MaxEmbedAttempts)
        {
            StopTimer();
            ShowPlaceholder(
                "The QEMU window was not found",
                "The guest process is running, but its SDL window did not appear within 10 seconds. Confirm this QEMU build includes SDL and that the window title contains NexusVM.");
            return;
        }

        var (width, height) = PixelSize();
        var attached = false;
        if (_strategy == SurfaceEmbedStrategy.Win32Reparent && _win32 is not null && _win32.Handle != IntPtr.Zero)
        {
            _win32Embedder ??= new Win32GuestWindowEmbedder();
            attached = _win32Embedder.TryAttach(_processId, _win32.Handle, _windowTitle);
            if (attached)
                _win32Embedder.Fit(_win32.Handle);
        }
        else if (_strategy == SurfaceEmbedStrategy.X11Reparent &&
                 _x11 is not null &&
                 _x11.Display != IntPtr.Zero &&
                 _x11.WindowHandle != IntPtr.Zero)
        {
            _x11Embedder ??= new X11GuestWindowEmbedder(_x11.Display);
            attached = _x11Embedder.TryAttach(_processId, _x11.WindowHandle, width, height);
        }

        if (!attached)
            return;

        _attached = true;
        StopTimer();
        Placeholder.IsVisible = false;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_attached)
            return;

        if (_win32Embedder is not null && _win32 is not null)
            _win32Embedder.Fit(_win32.Handle);

        var (width, height) = PixelSize();
        _x11Embedder?.Fit(width, height);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopTimer();
        base.OnDetachedFromVisualTree(e);
    }

    private void ApplyFrame(PixelFrame frame)
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != frame.Width || _bitmap.PixelSize.Height != frame.Height)
        {
            _bitmap = new WriteableBitmap(
                new PixelSize(frame.Width, frame.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
            FrameImage.Source = _bitmap;
        }

        var sourceStride = frame.Width * 4;
        using (var locked = _bitmap.Lock())
        {
            if (locked.RowBytes < sourceStride)
                return;

            for (var y = 0; y < frame.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    frame.Bgra32,
                    y * sourceStride,
                    IntPtr.Add(locked.Address, y * locked.RowBytes),
                    sourceStride);
            }
        }

        FrameImage.IsVisible = true;
        FrameImage.InvalidateVisual();
        Placeholder.IsVisible = false;
    }

    private void ShowPlaceholder(string title, string body)
    {
        PlaceholderTitle.Text = title;
        PlaceholderBody.Text = body;
        Placeholder.IsVisible = true;
    }

    private (int Width, int Height) PixelSize()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = Math.Max(1, (int)(Bounds.Width * scaling));
        var height = Math.Max(1, (int)(Bounds.Height * scaling));
        return (width, height);
    }

    private void StopTimer()
    {
        if (_timer is null)
            return;

        _timer.Stop();
        _timer.Tick -= OnEmbedTick;
    }

    private static int PpmLimit() => 4096;

    private static string QemuCommandBuilderTitle() => "NexusVM";
}
