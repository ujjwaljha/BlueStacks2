using Avalonia.Controls;
using Avalonia.Input;
using NexusVM.Core.Models;

namespace NexusVM.UI.Views;

public partial class DiagnosticWindow : Window
{
    public DiagnosticWindow()
    {
        InitializeComponent();
    }

    public static Task ShowAsync(Window owner, AccelerationDiagnosticException exception)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(exception);

        var window = new DiagnosticWindow
        {
            DataContext = DiagnosticPresentation.From(exception),
        };
        return window.ShowDialog(owner);
    }

    private void OnClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            Close();
    }
}
