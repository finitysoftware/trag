using Avalonia.Controls;
using Avalonia.Interactivity;
using Trag.Desktop.Platform;
using Trag.ViewModels.Setup;

namespace Trag.Desktop.Dialogs.Settings;

public sealed partial class GitPage : UserControl
{
    public GitPage() => InitializeComponent();

    // The file dialog belongs to the Settings window, so it opens over it.
    private async void ChooseGit(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GitSettingsViewModel model && TopLevel.GetTopLevel(this) is { } window && await GitExecutablePicker.PickAsync(window.StorageProvider) is { } path)
            await model.ChooseAsync(path);
    }
}
