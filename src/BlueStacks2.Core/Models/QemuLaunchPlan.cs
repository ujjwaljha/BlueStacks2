namespace BlueStacks2.Core.Models;

public sealed record QemuLaunchPlan
{
    public required string Binary { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>
    /// Quoted command for display. The process is started from <see cref="Arguments"/>,
    /// never by handing this string to a shell.
    /// </summary>
    public required string DisplayCommand { get; init; }

    public required HostPlatformKind Platform { get; init; }

    public required DisplayEmbedMode DisplayMode { get; init; }

    public required int QmpPort { get; init; }

    public required string WindowTitle { get; init; }
}
