namespace BlueStacks2.Core.Models;

public sealed class GuestLaunchRequest
{
    public string? SystemImagePath { get; init; }

    public string? DataImagePath { get; init; }

    /// <summary>
    /// AAVMF / QEMU_EFI firmware. Required for the Apple Silicon aarch64 GSI profile.
    /// </summary>
    public string? FirmwarePath { get; init; }
}
