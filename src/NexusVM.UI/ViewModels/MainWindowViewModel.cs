using NexusVM.Core.Engine;
using NexusVM.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexusVM.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly HostOrchestrator _orchestrator;

    public MainWindowViewModel(HostOrchestrator orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    [ObservableProperty]
    private string _hostSummary = "Detecting host…";

    [ObservableProperty]
    private string _architectureSummary = string.Empty;

    [ObservableProperty]
    private string _acceleratorSummary = string.Empty;

    [ObservableProperty]
    private string _acceleratorState = "Checking";

    [ObservableProperty]
    private bool _acceleratorReady;

    [ObservableProperty]
    private bool _showRemediation;

    [ObservableProperty]
    private string _remediation = string.Empty;

    [ObservableProperty]
    private string _systemImagePath = string.Empty;

    [ObservableProperty]
    private string _dataImagePath = string.Empty;

    [ObservableProperty]
    private string _firmwarePath = string.Empty;

    [ObservableProperty]
    private bool _firmwareRequired;

    [ObservableProperty]
    private string _commandPreview = "The QEMU command appears after a system image is chosen.";

    [ObservableProperty]
    private string _statusMessage = "Checking this host.";

    [ObservableProperty]
    private string _surfaceCaption = "Guest surface";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private SurfaceEmbedStrategy _surfaceStrategy;

    public AccelerationDiagnosticException? StartupDiagnostic { get; private set; }

    public event EventHandler? InspectionCompleted;

    public event EventHandler<AccelerationDiagnosticException>? DiagnosticRaised;

    public event EventHandler<PreparedLaunch>? LaunchRequested;

    public event EventHandler? StopRequested;

    public void Initialize()
    {
        var readiness = _orchestrator.Inspect();
        var environment = readiness.Environment;
        var acceleration = readiness.Acceleration;

        HostSummary = environment.Describe();
        ArchitectureSummary = $"OS {environment.OperatingSystemArchitecture} · process {environment.ProcessArchitecture}";
        AcceleratorSummary = acceleration.Summary;
        AcceleratorReady = acceleration.IsAvailable;
        AcceleratorState = acceleration.IsAvailable ? "Ready" : "Blocked";
        ShowRemediation = !acceleration.IsAvailable;
        Remediation = acceleration.Remediation ?? string.Empty;
        FirmwareRequired = environment.Platform == HostPlatformKind.MacOsArm64;
        SurfaceStrategy = HostProfiles.Surface(environment.Platform);
        SurfaceCaption = SurfaceStrategy switch
        {
            SurfaceEmbedStrategy.Win32Reparent => "Windows · WHPX · HWND reparent",
            SurfaceEmbedStrategy.X11Reparent => "Linux · KVM · X11 reparent",
            SurfaceEmbedStrategy.Framebuffer => "macOS · HVF · framebuffer",
            _ => "Unsupported host",
        };
        StatusMessage = acceleration.IsAvailable
            ? $"{environment.Describe()} · {acceleration.AcceleratorName}"
            : acceleration.Summary;
        StartupDiagnostic = acceleration.IsAvailable ? null : acceleration.ToException();
        UpdatePreview();
        InspectionCompleted?.Invoke(this, EventArgs.Empty);
    }

    public GuestLaunchRequest CreateRequest() => new()
    {
        SystemImagePath = NullIfBlank(SystemImagePath),
        DataImagePath = NullIfBlank(DataImagePath),
        FirmwarePath = NullIfBlank(FirmwarePath),
    };

    [RelayCommand]
    private void RefreshHost() => Initialize();

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private void Launch()
    {
        try
        {
            var prepared = _orchestrator.Prepare(CreateRequest());
            CommandPreview = prepared.Plan.DisplayCommand;
            IsRunning = true;
            StatusMessage = "Starting " + prepared.Plan.Binary + ".";
            LaunchRequested?.Invoke(this, prepared);
        }
        catch (AccelerationDiagnosticException ex)
        {
            IsRunning = false;
            CommandPreview = ex.Message;
            StatusMessage = ex.Title;
            DiagnosticRaised?.Invoke(this, ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        IsRunning = false;
        StatusMessage = "Stopping the guest.";
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanLaunch() => !IsRunning;

    private bool CanStop() => IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        LaunchCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    partial void OnSystemImagePathChanged(string value) => UpdatePreview();

    partial void OnDataImagePathChanged(string value) => UpdatePreview();

    partial void OnFirmwarePathChanged(string value) => UpdatePreview();

    private void UpdatePreview()
    {
        if (string.IsNullOrWhiteSpace(SystemImagePath) && string.IsNullOrWhiteSpace(FirmwarePath))
        {
            CommandPreview = FirmwareRequired
                ? "Choose an arm64 GSI and an AAVMF firmware image to preview the QEMU command."
                : "Choose a system image to preview the QEMU command.";
            return;
        }

        try
        {
            CommandPreview = _orchestrator.Prepare(CreateRequest()).Plan.DisplayCommand;
        }
        catch (AccelerationDiagnosticException ex)
        {
            CommandPreview = ex.Message;
        }
    }

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
