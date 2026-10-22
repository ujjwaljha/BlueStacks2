namespace NexusVM.Core.Models;

/// <summary>
/// Raised when a guest must not be launched. The shell shows <see cref="Title"/> and
/// <see cref="Remediation"/> in a diagnostic window instead of starting QEMU.
/// </summary>
public sealed class AccelerationDiagnosticException : Exception
{
    public AccelerationDiagnosticException(
        string title,
        string summary,
        string remediation,
        IReadOnlyList<string>? details = null,
        Exception? innerException = null)
        : base(summary, innerException)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Host diagnostic" : title;
        Remediation = string.IsNullOrWhiteSpace(remediation)
            ? "No automatic remediation is available for this host."
            : remediation;
        Details = details ?? Array.Empty<string>();
    }

    public string Title { get; }

    public string Remediation { get; }

    public IReadOnlyList<string> Details { get; }
}
