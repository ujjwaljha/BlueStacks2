namespace NexusVM.Core.Models;

public sealed record AccelerationProbeResult
{
    public required bool IsAvailable { get; init; }

    public required string AcceleratorName { get; init; }

    public required string Summary { get; init; }

    public string? DiagnosticTitle { get; init; }

    public string? Remediation { get; init; }

    public IReadOnlyList<string> Details { get; init; } = Array.Empty<string>();

    public static AccelerationProbeResult Available(string acceleratorName, string summary) =>
        new()
        {
            IsAvailable = true,
            AcceleratorName = acceleratorName,
            Summary = summary,
        };

    public static AccelerationProbeResult Unavailable(
        string acceleratorName,
        string title,
        string summary,
        string remediation,
        IReadOnlyList<string>? details = null) =>
        new()
        {
            IsAvailable = false,
            AcceleratorName = acceleratorName,
            DiagnosticTitle = title,
            Summary = summary,
            Remediation = remediation,
            Details = details ?? Array.Empty<string>(),
        };

    public AccelerationDiagnosticException ToException()
    {
        if (IsAvailable)
            throw new InvalidOperationException("The accelerator is available.");

        return new AccelerationDiagnosticException(
            DiagnosticTitle ?? "Acceleration unavailable",
            Summary,
            Remediation ?? "No automatic remediation is available for this host.",
            Details);
    }
}
