using Avalonia.Controls;
using Avalonia.Platform.Storage;
using BlueStacks2.App.Session;
using BlueStacks2.App.ViewModels;
using BlueStacks2.Core.Launch;
using BlueStacks2.Core.Models;

namespace BlueStacks2.App.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private GuestSession? _session;
    private bool _dialogOpen;
    private bool _userStop;

    public MainWindow()
        : this(HostOrchestrator.CreateDefault())
    {
    }

    public MainWindow(HostOrchestrator orchestrator)
    {
        _viewModel = new MainWindowViewModel(orchestrator);
        _viewModel.DiagnosticRaised += OnDiagnosticRaised;
        _viewModel.LaunchRequested += OnLaunchRequested;
        _viewModel.StopRequested += OnStopRequested;
        _viewModel.InspectionCompleted += OnInspectionCompleted;
        InitializeComponent();
        DataContext = _viewModel;
    }

    public MainWindowViewModel ViewModel => _viewModel;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _viewModel.Initialize();
    }

    protected override void OnClosed(EventArgs e)
    {
        _userStop = true;
        var session = _session;
        _session = null;
        if (session is not null)
        {
            try
            {
                session.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(4));
            }
            catch (Exception)
            {
                // Shutdown already asked the guest to quit.
            }
        }

        base.OnClosed(e);
    }

    private void OnInspectionCompleted(object? sender, EventArgs e)
    {
        try
        {
            Surface.ApplyStrategy(_viewModel.SurfaceStrategy);
        }
        catch (Exception ex)
        {
            Surface.ShowFault("Guest surface unavailable", ex.Message);
        }

        if (_viewModel.StartupDiagnostic is { } diagnostic)
            _ = ShowDiagnosticAsync(diagnostic);
    }

    private void OnDiagnosticRaised(object? sender, AccelerationDiagnosticException exception) =>
        _ = ShowDiagnosticAsync(exception);

    private void OnLaunchRequested(object? sender, PreparedLaunch launch)
    {
        try
        {
            _userStop = false;
            _session = GuestSession.Start(launch, Surface);
            _session.Exited += OnGuestExited;
            _viewModel.StatusMessage = $"Guest running · pid {_session.ProcessId} · {launch.Plan.Binary}";
        }
        catch (Exception ex)
        {
            _viewModel.IsRunning = false;
            _viewModel.StatusMessage = "The guest did not start.";
            _ = ShowDiagnosticAsync(ToDiagnostic(ex));
        }
    }

    private async void OnStopRequested(object? sender, EventArgs e)
    {
        _userStop = true;
        var session = _session;
        _session = null;
        if (session is not null)
            await session.DisposeAsync().ConfigureAwait(true);

        _userStop = false;
        Surface.ShowIdle();
        _viewModel.StatusMessage = "Guest stopped.";
    }

    private void OnGuestExited(object? sender, int code)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var session = _session;
            _session = null;
            _viewModel.IsRunning = false;
            if (_userStop || session is null)
                return;

            var log = session.ReadLog();
            var details = string.IsNullOrWhiteSpace(log)
                ? new[] { "exitCode=" + code }
                : new[] { "exitCode=" + code, log };
            _ = ShowDiagnosticAsync(new AccelerationDiagnosticException(
                "QEMU exited",
                $"The guest process exited with code {code}.",
                "Read the QEMU log below. A missing accelerator, an unreadable image, or a display backend this QEMU build does not include are the usual causes.",
                details));
            _ = session.DisposeAsync();
        });
    }

    private async void OnBrowseSystem(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await BrowseAsync("System image", new[] { "*.img", "*.raw", "*.qcow2", "*.iso" }, path => _viewModel.SystemImagePath = path);

    private async void OnBrowseData(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await BrowseAsync("Data image", new[] { "*.img", "*.raw", "*.qcow2" }, path => _viewModel.DataImagePath = path);

    private async void OnBrowseFirmware(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await BrowseAsync("UEFI firmware", new[] { "*.fd", "*.bin" }, path => _viewModel.FirmwarePath = path);

    private async Task BrowseAsync(string title, IReadOnlyList<string> patterns, Action<string> assign)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(title) { Patterns = patterns },
                    FilePickerFileTypes.All,
                },
            });

            if (files.Count == 0)
                return;

            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                await ShowDiagnosticAsync(new AccelerationDiagnosticException(
                    "The file has no local path",
                    "QEMU needs a filesystem path, and the selected item did not provide one.",
                    "Choose a file on a local disk."));
                return;
            }

            assign(path);
        }
        catch (Exception ex)
        {
            await ShowDiagnosticAsync(ToDiagnostic(ex));
        }
    }

    private async Task ShowDiagnosticAsync(AccelerationDiagnosticException exception)
    {
        if (_dialogOpen)
            return;

        _dialogOpen = true;
        try
        {
            await DiagnosticWindow.ShowAsync(this, exception);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private static AccelerationDiagnosticException ToDiagnostic(Exception exception)
    {
        if (exception is AccelerationDiagnosticException diagnostic)
            return diagnostic;

        return new AccelerationDiagnosticException(
            "The guest could not be started",
            exception.Message,
            "Check the QEMU installation, the image paths, and the accelerator status for this host.",
            new[] { exception.GetType().Name },
            exception);
    }
}
