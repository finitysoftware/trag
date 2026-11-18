using Avalonia.Controls;
using Avalonia.Input;

namespace Trag.Desktop.Controls;

// The menu key asks the focused row of a list for its menu, which the row's template holds on its content. A focused row
// outside the selection becomes the selection first, as a right-click makes it, so the menu acts on that row.
public static class RowMenu
{
    public static void Open(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not ListBox list || e.Source is not ListBoxItem { Presenter.Child: { ContextFlyout: { } menu } content } row) return;
        if (!row.IsSelected) list.SelectedItem = row.DataContext;
        menu.ShowAt(content);
        e.Handled = true;
    }
}
