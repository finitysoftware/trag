using Avalonia.Controls;
using Avalonia.Input;
using Trag.ViewModels.Repositories;

namespace Trag.Desktop.Views.Repositories;

public sealed partial class LocalRepositoryList : UserControl
{
    public LocalRepositoryList() => InitializeComponent();
    // Double-click on a local entry opens it.
    private void OpenOnDoubleTap(object? sender, TappedEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is LocalRepositoryRow local) ((RepositoryCatalogViewModel)DataContext!).OpenEntryCommand.Execute(local);
    }
    // A left press anywhere on the header folds or opens the list, as the chevron does; the chevron takes its own press.
    private void FoldOnHeaderPress(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && LocalFoldButton.IsVisible) LocalFoldButton.Command!.Execute(null);
    }
}
