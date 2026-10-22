using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using NexusVM.Core.Input;

namespace NexusVM.UI.ViewModels;

public sealed class KeymapEditorViewModel : ObservableObject
{
    public KeymapEditorViewModel(KeymapProfile? profile)
    {
        ProfileName = profile?.ProfileName ?? "Standard FPS Profile";
        Rows = new ObservableCollection<KeymapEditorRow>(
            profile?.Mappings.Select(KeymapEditorRow.From) ?? Array.Empty<KeymapEditorRow>());
        if (Rows.Count == 0)
            Rows.Add(KeymapEditorRow.Tap());
    }

    public string ProfileName { get; set; }

    public ObservableCollection<KeymapEditorRow> Rows { get; }

    public string Error { get; private set; } = string.Empty;

    public void AddTap() => Rows.Add(KeymapEditorRow.Tap());

    public void Remove(KeymapEditorRow row) => Rows.Remove(row);

    public bool TryBuild(out KeymapProfile? profile)
    {
        profile = null;
        Error = string.Empty;
        if (Rows.Count == 0)
        {
            Error = "Add at least one mapping.";
            OnPropertyChanged(nameof(Error));
            return false;
        }

        try
        {
            var draft = new KeymapProfileBuilder(ProfileName);
            foreach (var row in Rows)
                draft.Add(row);
            profile = KeymapProfile.Parse(draft.ToJson());
            return true;
        }
        catch (Exception ex) when (ex is KeymapFormatException or FormatException or ArgumentException)
        {
            Error = ex.Message;
            OnPropertyChanged(nameof(Error));
            return false;
        }
    }

    private sealed class KeymapProfileBuilder
    {
        private readonly string _name;
        private readonly List<KeymapEditorRow> _rows = new();

        public KeymapProfileBuilder(string name) => _name = name;

        public void Add(KeymapEditorRow row) => _rows.Add(row);

        public string ToJson()
        {
            var mappings = new List<Dictionary<string, object?>>();
            foreach (var row in _rows)
            {
                var action = row.Action.Trim().ToLowerInvariant();
                var entry = new Dictionary<string, object?>
                {
                    ["key"] = row.Key.Trim(),
                    ["action"] = action,
                    ["label"] = row.Label.Trim(),
                };
                if (action == "tap")
                {
                    entry["targetX"] = Parse(row.TargetX, "targetX");
                    entry["targetY"] = Parse(row.TargetY, "targetY");
                }
                else if (action == "swipe")
                {
                    entry["startX"] = Parse(row.StartX, "startX");
                    entry["startY"] = Parse(row.StartY, "startY");
                    entry["endX"] = Parse(row.EndX, "endX");
                    entry["endY"] = Parse(row.EndY, "endY");
                }

                mappings.Add(entry);
            }

            return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["profileName"] = _name,
                ["mappings"] = mappings,
            });
        }

        private static int Parse(string text, string field)
        {
            if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"{field} must be a whole number.");

            return value;
        }
    }
}

public sealed partial class KeymapEditorRow : ObservableObject
{
    [ObservableProperty]
    private string _key = "W";

    [ObservableProperty]
    private string _action = "tap";

    [ObservableProperty]
    private string _targetX = "0";

    [ObservableProperty]
    private string _targetY = "0";

    [ObservableProperty]
    private string _startX = "0";

    [ObservableProperty]
    private string _startY = "0";

    [ObservableProperty]
    private string _endX = "0";

    [ObservableProperty]
    private string _endY = "0";

    [ObservableProperty]
    private string _label = string.Empty;

    public static KeymapEditorRow Tap() => new()
    {
        Key = "F",
        Action = "tap",
        TargetX = "960",
        TargetY = "540",
        Label = "Action",
    };

    public static KeymapEditorRow From(KeyMapping mapping)
    {
        return new KeymapEditorRow
        {
            Key = mapping.Key,
            Action = mapping.Action,
            TargetX = mapping.TargetX.ToString(CultureInfo.InvariantCulture),
            TargetY = mapping.TargetY.ToString(CultureInfo.InvariantCulture),
            StartX = mapping.StartX.ToString(CultureInfo.InvariantCulture),
            StartY = mapping.StartY.ToString(CultureInfo.InvariantCulture),
            EndX = mapping.EndX.ToString(CultureInfo.InvariantCulture),
            EndY = mapping.EndY.ToString(CultureInfo.InvariantCulture),
            Label = mapping.Label,
        };
    }
}
