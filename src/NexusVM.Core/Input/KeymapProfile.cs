using System.Globalization;
using System.Text.Json;

namespace NexusVM.Core.Input;

public sealed class KeymapProfile
{
    private readonly Dictionary<string, KeyMapping> _byKey;

    private KeymapProfile(string profileName, IReadOnlyList<KeyMapping> mappings)
    {
        ProfileName = profileName;
        Mappings = mappings;
        _byKey = new Dictionary<string, KeyMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            if (!_byKey.TryAdd(mapping.Key, mapping))
                throw new KeymapFormatException($"Key '{mapping.Key}' is used more than once.");
        }
    }

    public string ProfileName { get; }

    public IReadOnlyList<KeyMapping> Mappings { get; }

    public bool TryGet(string key, out KeyMapping? mapping)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            mapping = null;
            return false;
        }

        if (_byKey.TryGetValue(key.Trim(), out var found))
        {
            mapping = found;
            return true;
        }

        mapping = null;
        return false;
    }

    public static KeymapProfile Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        KeymapDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<KeymapDocument>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new KeymapFormatException("The keymap file is not valid JSON.", ex);
        }

        if (document is null || string.IsNullOrWhiteSpace(document.ProfileName))
            throw new KeymapFormatException("The keymap needs a profileName.");

        if (document.Mappings is null || document.Mappings.Count == 0)
            throw new KeymapFormatException("The keymap needs at least one mapping.");

        var mappings = new List<KeyMapping>(document.Mappings.Count);
        foreach (var entry in document.Mappings)
            mappings.Add(KeyMapping.From(entry));

        return new KeymapProfile(document.ProfileName.Trim(), mappings);
    }

    public static KeymapProfile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path));
    }

    public string ToJson()
    {
        var mappings = new List<Dictionary<string, object?>>(Mappings.Count);
        foreach (var mapping in Mappings)
        {
            var entry = new Dictionary<string, object?>
            {
                ["key"] = mapping.Key,
                ["action"] = mapping.Action,
                ["label"] = mapping.Label,
            };
            if (mapping.Action == "tap")
            {
                entry["targetX"] = mapping.TargetX;
                entry["targetY"] = mapping.TargetY;
            }
            else
            {
                entry["startX"] = mapping.StartX;
                entry["startY"] = mapping.StartY;
                entry["endX"] = mapping.EndX;
                entry["endY"] = mapping.EndY;
            }

            mappings.Add(entry);
        }

        var root = new Dictionary<string, object?>
        {
            ["profileName"] = ProfileName,
            ["mappings"] = mappings,
        };
        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class KeymapDocument
    {
        public string? ProfileName { get; set; }

        public List<KeymapEntry>? Mappings { get; set; }
    }

    internal sealed class KeymapEntry
    {
        public string? Key { get; set; }

        public string? Action { get; set; }

        public int? TargetX { get; set; }

        public int? TargetY { get; set; }

        public int? StartX { get; set; }

        public int? StartY { get; set; }

        public int? EndX { get; set; }

        public int? EndY { get; set; }

        public string? Label { get; set; }
    }
}

public sealed class KeyMapping
{
    public const int MaxCoordinate = 4096;

    private KeyMapping(
        string key,
        string action,
        int targetX,
        int targetY,
        int startX,
        int startY,
        int endX,
        int endY,
        string label)
    {
        Key = key;
        Action = action;
        TargetX = targetX;
        TargetY = targetY;
        StartX = startX;
        StartY = startY;
        EndX = endX;
        EndY = endY;
        Label = label;
    }

    public string Key { get; }

    public string Action { get; }

    public int TargetX { get; }

    public int TargetY { get; }

    public int StartX { get; }

    public int StartY { get; }

    public int EndX { get; }

    public int EndY { get; }

    public string Label { get; }

    internal static KeyMapping From(KeymapProfile.KeymapEntry entry)
    {
        var key = RequireToken(entry.Key, "key");
        var action = entry.Action?.Trim().ToLowerInvariant();
        var label = string.IsNullOrWhiteSpace(entry.Label) ? key : entry.Label.Trim();
        if (label.Length > 80)
            throw new KeymapFormatException($"The label for '{key}' is too long.");

        return action switch
        {
            "tap" => new KeyMapping(
                key,
                "tap",
                RequireCoordinate(entry.TargetX, key, "targetX"),
                RequireCoordinate(entry.TargetY, key, "targetY"),
                0,
                0,
                0,
                0,
                label),
            "swipe" => new KeyMapping(
                key,
                "swipe",
                0,
                0,
                RequireCoordinate(entry.StartX, key, "startX"),
                RequireCoordinate(entry.StartY, key, "startY"),
                RequireCoordinate(entry.EndX, key, "endX"),
                RequireCoordinate(entry.EndY, key, "endY"),
                label),
            _ => throw new KeymapFormatException($"Mapping '{key}' must use action tap or swipe."),
        };
    }

    private static string RequireToken(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new KeymapFormatException($"A mapping is missing {field}.");

        var token = value.Trim();
        if (token.Length is < 1 or > 32)
            throw new KeymapFormatException($"'{token}' is not a usable {field}.");

        foreach (var character in token)
        {
            if (!char.IsAsciiLetterOrDigit(character))
                throw new KeymapFormatException($"'{token}' may contain only letters and digits.");
        }

        return token;
    }

    private static int RequireCoordinate(int? value, string key, string field)
    {
        if (value is not int coordinate)
            throw new KeymapFormatException($"Mapping '{key}' is missing {field}.");

        if (coordinate is < 0 or > MaxCoordinate)
            throw new KeymapFormatException($"Mapping '{key}' has {field} outside 0..{MaxCoordinate.ToString(CultureInfo.InvariantCulture)}.");

        return coordinate;
    }
}

public sealed class KeymapFormatException : Exception
{
    public KeymapFormatException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
