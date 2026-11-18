using Avalonia.Controls;
using Avalonia.Input;
using Trag.ViewModels.Repositories;

namespace Trag.Desktop.Views.Repositories;

public sealed partial class AccountGroupSection : UserControl
{
    public AccountGroupSection() => InitializeComponent();
    // Double-click on a hosted repository opens its clone or starts one.
    private void OpenOnDoubleTap(object? sender, TappedEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is RemoteRepositoryRow remote) ((RepositoryCatalogViewModel)DataContext!).ActivateRemoteCommand.Execute(remote);
    }
    // A left press anywhere on an account's header folds or opens its list once it has rows to fold; the header's buttons
    // take their own presses.
    private void FoldOnHeaderPress(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && sender is Control { DataContext: AccountGroup { HasVisibleRows: true } group }) group.Fold.ToggleCommand.Execute(null);
    }
}
