using Avalonia;

namespace BlueStacks2.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        if (OperatingSystem.IsLinux())
        {
            // UsePlatformDetect already selects X11 on Linux today. Pin it so a future
            // Wayland default cannot take away the XIDs QEMU's SDL window is reparented into.
            builder = builder.UseX11();
        }

        return builder;
    }
}
