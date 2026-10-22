namespace BlueStacks2.Core.Launch;

/// <summary>
/// QEMU option values are comma-separated. A guest path is data, never a second option.
/// </summary>
public static class QemuOptionSyntax
{
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new ArgumentException("The value contains a control character.", nameof(value));

        return value.Replace(",", ",,", StringComparison.Ordinal);
    }

    public static string DiskFormat(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".qcow2", StringComparison.OrdinalIgnoreCase) ? "qcow2" : "raw";
    }

    public static string Drive(string id, string format, string file, bool readOnly)
    {
        if (id is not ("system" or "userdata"))
            throw new ArgumentOutOfRangeException(nameof(id));

        if (format is not ("raw" or "qcow2"))
            throw new ArgumentOutOfRangeException(nameof(format));

        var mode = readOnly ? "on" : "off";
        return $"if=none,id={id},format={format},readonly={mode},file={Escape(file)}";
    }

    public static string FormatCommand(string binary, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binary);
        ArgumentNullException.ThrowIfNull(arguments);

        var parts = new List<string>(arguments.Count + 1) { Quote(binary) };
        foreach (var argument in arguments)
            parts.Add(Quote(argument));

        return string.Join(' ', parts);
    }

    public static string Quote(string value)
    {
        if (value.Length == 0)
            return "\"\"";

        var safe = true;
        foreach (var character in value)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '/' or ':' or ',' or '=' or '+'))
            {
                safe = false;
                break;
            }
        }

        if (safe)
            return value;

        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
