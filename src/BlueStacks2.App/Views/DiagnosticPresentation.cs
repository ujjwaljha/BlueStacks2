using BlueStacks2.Core.Models;

namespace BlueStacks2.App.Views;

public sealed class DiagnosticPresentation
{
    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required string Remediation { get; init; }

    public required IReadOnlyList<string> Details { get; init; }

    public bool HasDetails => Details.Count > 0;

    public static DiagnosticPresentation From(AccelerationDiagnosticException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new DiagnosticPresentation
        {
            Title = exception.Title,
            Summary = exception.Message,
            Remediation = exception.Remediation,
            Details = exception.Details,
        };
    }
}
