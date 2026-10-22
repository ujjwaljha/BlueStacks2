using BlueStacks2.Core.Models;

namespace BlueStacks2.Core.Launch;

public interface IQemuBinaryLocator
{
    string? Locate(string binaryName);
}

public sealed class PathQemuBinaryLocator : IQemuBinaryLocator
{
    private readonly string? _pathOverride;

    public PathQemuBinaryLocator(string? pathOverride = null)
    {
        _pathOverride = pathOverride;
    }

    public string? Locate(string binaryName)
    {
        if (binaryName is not (HostProfiles.X86Binary or HostProfiles.Aarch64Binary))
            throw new ArgumentOutOfRangeException(nameof(binaryName), "QEMU binary is not one of the supported host binaries.");

        var path = _pathOverride ?? Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;

        var names = new List<string> { binaryName };
        if (OperatingSystem.IsWindows())
            names.Add(binaryName + ".exe");

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
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
        }

        return null;
    }
}
