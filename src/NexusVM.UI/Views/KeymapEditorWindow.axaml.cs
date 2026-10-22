using Avalonia.Controls;
using Avalonia.Interactivity;
using NexusVM.Core.Input;
using NexusVM.UI.ViewModels;

namespace NexusVM.UI.Views;

public partial class KeymapEditorWindow : Window
{
    private readonly KeymapEditorViewModel _model;

    public KeymapEditorWindow()
        : this(null)
    {
    }

    public KeymapEditorWindow(KeymapProfile? profile)
    {
        _model = new KeymapEditorViewModel(profile);
        InitializeComponent();
        DataContext = _model;
    }

    public KeymapProfile? Saved { get; private set; }

    private void OnAdd(object? sender, RoutedEventArgs e) => _model.AddTap();

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: KeymapEditorRow row })
            _model.Remove(row);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (!_model.TryBuild(out var profile) || profile is null)
            return;

        Saved = profile;
        Close();
    }
}
