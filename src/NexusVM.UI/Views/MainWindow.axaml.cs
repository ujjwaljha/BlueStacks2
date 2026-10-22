using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using NexusVM.Core.Engine;
using NexusVM.Core.Input;
using NexusVM.Core.Models;
using NexusVM.UI.Session;
using NexusVM.UI.ViewModels;

namespace NexusVM.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly AdbProcessClient _adb = new();
    private GuestSession? _session;
    private bool _dialogOpen;
    private bool _userStop;

    public MainWindow()
        : this(HostOrchestrator.CreateDefault(), LaunchSettings.DefaultPath())
    {
    }

    public MainWindow(HostOrchestrator orchestrator, string? settingsPath = null)
    {
        _viewModel = new MainWindowViewModel(orchestrator, settingsPath);
        _viewModel.DiagnosticRaised += OnDiagnosticRaised;
        _viewModel.LaunchRequested += OnLaunchRequested;
        _viewModel.StopRequested += OnStopRequested;
        _viewModel.InspectionCompleted += OnInspectionCompleted;
        InitializeComponent();
        DataContext = _viewModel;
        Surface.PointerPressed += OnSurfacePointerPressed;
    }

    public MainWindowViewModel ViewModel => _viewModel;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Source is TextBox || _session is null || _viewModel.Keymap is null)
            return;

        if (!_viewModel.Keymap.TryGet(e.Key.ToString(), out var mapping) || mapping is null)
            return;

        e.Handled = true;
        _ = SendInputAsync(mapping);
    }

    private void OnBack(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _ = SendKeyAsync(AdbInput.KeyBack);

    private void OnHome(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _ = SendKeyAsync(AdbInput.KeyHome);

    private void OnRecents(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _ = SendKeyAsync(AdbInput.KeyRecents);

    private async Task SendKeyAsync(int keyCode)
    {
        var session = _session;
        if (session is null)
            return;

        try
        {
            var serial = AdbInput.LoopbackSerial(session.AdbHostPort);
            var result = await _adb.RunAsync(AdbInput.KeyEventCommand(serial, keyCode), CancellationToken.None).ConfigureAwait(true);
            _viewModel.StatusMessage = result.ExitCode == 0
                ? $"Sent key {keyCode} to {serial}."
                : $"adb could not send key {keyCode}. {result.Output}".Trim();
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = ex.Message;
        }
    }

    private void OnSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_session is null || _viewModel.Keymap is null)
            return;

        if (!e.GetCurrentPoint(Surface).Properties.IsLeftButtonPressed)
            return;

        if (!_viewModel.Keymap.TryGet("LeftClick", out var mapping) || mapping is null)
            return;

        _ = SendInputAsync(mapping);
    }

    private async Task SendInputAsync(KeyMapping mapping)
    {
        var session = _session;
        if (session is null)
            return;

        try
        {
            var serial = AdbInput.LoopbackSerial(session.AdbHostPort);
            var result = await _adb.RunAsync(AdbInput.Command(serial, mapping), CancellationToken.None).ConfigureAwait(true);
            _viewModel.StatusMessage = result.ExitCode == 0
                ? $"Sent {mapping.Label} to {serial}."
                : $"adb could not send {mapping.Label}. {result.Output}".Trim();
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = ex.Message;
        }
    }

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
            _viewModel.ClearGuestLog();
            _session = GuestSession.Start(launch, Surface);
            _session.Exited += OnGuestExited;
            _session.LogReceived += OnGuestLog;
            _viewModel.StatusMessage = $"Guest running · pid {_session.ProcessId} · {launch.Plan.Binary}";
            _ = ConnectAdbAsync(_session);
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
        {
            await DisconnectAdbAsync(session).ConfigureAwait(true);
            await session.DisposeAsync().ConfigureAwait(true);
        }

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

            _ = DisconnectAdbAsync(session);
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

    private async void OnEditKeymap(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new KeymapEditorWindow(_viewModel.Keymap);
        await dialog.ShowDialog(this);
        if (dialog.Saved is not { } profile)
            return;

        try
        {
            _viewModel.ReplaceKeymap(profile);
            _viewModel.StatusMessage = "Saved keymap " + profile.ProfileName + ".";
        }
        catch (Exception ex)
        {
            await ShowDiagnosticAsync(ToDiagnostic(ex));
        }
    }

    private void OnGuestLog(object? sender, string line)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _viewModel.AppendGuestLog(line));
    }

    private async Task ConnectAdbAsync(GuestSession session)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try
        {
            var serial = AdbInput.LoopbackSerial(session.AdbHostPort);
            var connect = await _adb.RunAsync(AdbInput.ConnectCommand(serial), timeout.Token).ConfigureAwait(true);
            if (!ReferenceEquals(_session, session))
                return;

            if (connect.ExitCode != 0)
            {
                _viewModel.StatusMessage = $"Guest running. adb connect failed. {connect.Output}".Trim();
                return;
            }

            var state = "absent";
            for (var attempt = 0; attempt < 6 && ReferenceEquals(_session, session); attempt++)
            {
                var listed = await _adb.RunAsync(AdbInput.DevicesCommand(), timeout.Token).ConfigureAwait(true);
                state = AdbInput.DeviceState(listed.Output, serial);
                if (state == "device")
                    break;

                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token).ConfigureAwait(true);
            }

            if (!ReferenceEquals(_session, session))
                return;

            _viewModel.StatusMessage = state switch
            {
                "device" => $"Guest running · adb {serial} is online.",
                "unauthorized" => $"Guest running · adb {serial} is unauthorized. Accept the guest prompt.",
                "offline" => $"Guest running · adb {serial} is offline. adbd may still be starting.",
                _ => $"Guest running · adb {serial} has not appeared yet.",
            };
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_session, session))
                _viewModel.StatusMessage = "Guest running. " + ex.Message;
        }
    }

    private async Task DisconnectAdbAsync(GuestSession session)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var serial = AdbInput.LoopbackSerial(session.AdbHostPort);
            await _adb.RunAsync(AdbInput.DisconnectCommand(serial), timeout.Token).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Stopping the guest does not depend on adb answering.
        }
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
