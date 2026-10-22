using NexusVM.Core.Models;

namespace NexusVM.Core.Engine;

public interface IQemuBinaryLocator
{
    string? Locate(string binaryName);
}

public sealed class PathQemuBinaryLocator : IQemuBinaryLocator
{
    private readonly string? _pathOverride;
    private readonly IReadOnlyList<string> _bundledDirectories;

    public PathQemuBinaryLocator(string? pathOverride = null, IReadOnlyList<string>? bundledDirectories = null)
    {
        _pathOverride = pathOverride;
        _bundledDirectories = bundledDirectories ?? DefaultBundledDirectories();
    }

    public static IReadOnlyList<string> DefaultBundledDirectories() => new[]
    {
        Path.Combine(AppContext.BaseDirectory, "assets", "bin"),
        Path.Combine(Directory.GetCurrentDirectory(), "assets", "bin"),
    };

    public string? Locate(string binaryName)
    {
        if (binaryName is not (HostProfiles.X86Binary or HostProfiles.Aarch64Binary))
            throw new ArgumentOutOfRangeException(nameof(binaryName), "QEMU binary is not one of the supported host binaries.");

        var names = new List<string> { binaryName };
        if (OperatingSystem.IsWindows())
            names.Add(binaryName + ".exe");

        foreach (var directory in _bundledDirectories)
        {
            var found = FindInDirectory(directory, names);
            if (found is not null)
                return found;
        }

        var path = _pathOverride ?? Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var found = FindInDirectory(directory, names);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static string? FindInDirectory(string directory, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(directory, name));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
