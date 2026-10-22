using System.Text.Json;

namespace NexusVM.Core.Engine;

public sealed class LaunchSettings
{
    public string? SystemImagePath { get; init; }

    public string? DataImagePath { get; init; }

    public string? FirmwarePath { get; init; }

    public static string DefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexusVM",
            "launch.json");

    public static LaunchSettings Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            return new LaunchSettings();

        try
        {
            var json = File.ReadAllText(path);
            var stored = JsonSerializer.Deserialize<Stored>(json, Options);
            return new LaunchSettings
            {
                SystemImagePath = Clean(stored?.SystemImagePath),
                DataImagePath = Clean(stored?.DataImagePath),
                FirmwarePath = Clean(stored?.FirmwarePath),
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new LaunchSettings();
        }
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(new Stored
        {
            SystemImagePath = Clean(SystemImagePath),
            DataImagePath = Clean(DataImagePath),
            FirmwarePath = Clean(FirmwarePath),
        }, Options);
        File.WriteAllText(path, json + Environment.NewLine);
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.IndexOfAny(['\0', '\r', '\n']) >= 0)
            return null;

        return value.Trim();
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed class Stored
    {
        public string? SystemImagePath { get; set; }

        public string? DataImagePath { get; set; }

        public string? FirmwarePath { get; set; }
    }
}
