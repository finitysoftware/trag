using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Controls;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.Views.History;

public sealed partial class CommitListPanel : UserControl
{
    public CommitListPanel()
    {
        InitializeComponent();
        HistoryList.ContextRequested += RowMenu.Open;
        HistoryList.AddHandler(ScrollViewer.ScrollChangedEvent, HistoryScrolled);
    }

    // The next page is read while the rows still to come below fill less than the list's own height, which a list too
    // short to scroll does from the start; the command runs once at a time and not once the whole history is shown.
    private void HistoryScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer list || DataContext is not HistoryPage page) return;
        if (list.Extent.Height - list.Offset.Y - list.Viewport.Height > list.Viewport.Height) return;
        if (page.History.LoadMoreHistoryCommand.CanExecute(null)) _ = page.History.LoadMoreHistoryCommand.ExecuteAsync(null);
    }
    // Double-click on a row goes to that commit; empty space and the scrollbar do nothing.
    private void HistoryRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source || source.FindAncestorOfType<ListBoxItem>(true) is null || DataContext is not HistoryPage page) return;
        e.Handled = true;
        _ = page.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null);
    }
}
