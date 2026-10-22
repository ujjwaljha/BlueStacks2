using NexusVM.Core.Engine;
using NexusVM.Core.Input;
using NexusVM.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexusVM.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly HostOrchestrator _orchestrator;
    private readonly string? _settingsPath;
    private bool _settingsReady;

    public MainWindowViewModel(HostOrchestrator orchestrator, string? settingsPath = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _settingsPath = settingsPath;
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
    private string _guestLog = string.Empty;

    [ObservableProperty]
    private string _surfaceCaption = "Guest surface";

    [ObservableProperty]
    private string _keymapTitle = "No keymap loaded";

    public KeymapProfile? Keymap { get; private set; }

    public string? KeymapPath { get; private set; }

    public IReadOnlyList<KeymapRow> KeymapRows { get; private set; } = Array.Empty<KeymapRow>();

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
        LoadSavedPaths();
        LoadKeymap();
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

    partial void OnSystemImagePathChanged(string value)
    {
        UpdatePreview();
        SavePaths();
    }

    partial void OnDataImagePathChanged(string value)
    {
        UpdatePreview();
        SavePaths();
    }

    partial void OnFirmwarePathChanged(string value)
    {
        UpdatePreview();
        SavePaths();
    }

    private void LoadSavedPaths()
    {
        if (_settingsReady || string.IsNullOrWhiteSpace(_settingsPath))
            return;

        _settingsReady = true;
        var saved = LaunchSettings.Load(_settingsPath);
        if (string.IsNullOrWhiteSpace(SystemImagePath) && saved.SystemImagePath is not null)
            SystemImagePath = saved.SystemImagePath;
        if (string.IsNullOrWhiteSpace(DataImagePath) && saved.DataImagePath is not null)
            DataImagePath = saved.DataImagePath;
        if (string.IsNullOrWhiteSpace(FirmwarePath) && saved.FirmwarePath is not null)
            FirmwarePath = saved.FirmwarePath;
    }

    private void SavePaths()
    {
        if (!_settingsReady || string.IsNullOrWhiteSpace(_settingsPath))
            return;

        new LaunchSettings
        {
            SystemImagePath = NullIfBlank(SystemImagePath),
            DataImagePath = NullIfBlank(DataImagePath),
            FirmwarePath = NullIfBlank(FirmwarePath),
        }.Save(_settingsPath);
    }

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

    public void ClearGuestLog() => GuestLog = string.Empty;

    public void AppendGuestLog(string line)
    {
        if (string.IsNullOrEmpty(line))
            return;

        var next = string.IsNullOrEmpty(GuestLog) ? line : GuestLog + Environment.NewLine + line;
        const int cap = 12000;
        GuestLog = next.Length <= cap ? next : next[^cap..];
    }

    public void ReplaceKeymap(KeymapProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var path = KeymapPath ?? Path.Combine(AppContext.BaseDirectory, "assets", "keymaps", "default.json");
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, profile.ToJson() + Environment.NewLine);
        KeymapPath = path;
        ApplyProfile(profile);
    }

    private void LoadKeymap()
    {
        var path = FindKeymap();
        KeymapPath = path;
        if (path is null)
        {
            Keymap = null;
            KeymapTitle = "No keymap file";
            ReplaceRows(Array.Empty<KeymapRow>());
            return;
        }

        try
        {
            ApplyProfile(KeymapProfile.Load(path));
        }
        catch (Exception ex) when (ex is KeymapFormatException or IOException)
        {
            Keymap = null;
            KeymapTitle = "Keymap could not be read";
            ReplaceRows(new[] { new KeymapRow { Gesture = "Error", Detail = ex.Message } });
        }
    }

    private void ApplyProfile(KeymapProfile profile)
    {
        Keymap = profile;
        KeymapTitle = profile.ProfileName;
        ReplaceRows(profile.Mappings.Select(mapping => new KeymapRow
        {
            Gesture = mapping.Key,
            Detail = mapping.Label,
        }).ToArray());
    }

    private void ReplaceRows(IReadOnlyList<KeymapRow> rows)
    {
        KeymapRows = rows;
        OnPropertyChanged(nameof(KeymapRows));
    }

    private static string? FindKeymap()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "keymaps", "default.json"),
            Path.Combine(AppContext.BaseDirectory, "assets", "keymaps", "default.json"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}

public sealed class KeymapRow
{
    public required string Gesture { get; init; }

    public required string Detail { get; init; }
}
