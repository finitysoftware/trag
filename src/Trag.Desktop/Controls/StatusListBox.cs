using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Trag.ViewModels.Status;

namespace Trag.Desktop.Controls;

// File rows have a uniform height. Anchor the visible file when rows ahead of it
// are inserted/removed, while retaining the native list's keyboard behavior.
public sealed class StatusListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);
    private StatusList? source;
    private ScrollViewer? scroller;
    private StatusRow? anchor;
    private int anchorIndex;
    private double rowHeight;
    private Vector offset;
    // The selected row and all the selected rows when a batch of edits started. A row the edits replaced leaves the
    // selection, and the file status selects it again once the lists are the snapshot's; the list would scroll to it then,
    // so scrolling to the selection waits for the selected row chosen anew when it was among them (2) and for the
    // selection after it (1), the first that is not the update's. A selection the edits themselves move is neither.
    private StatusRow? selected;
    private StatusRow[] rows = [];
    private int held;
    private bool editing;
    public StatusListBox() => ContextRequested += RowMenu.Open;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedItemProperty && held > 0 && !editing && --held == 0) AutoScrollToSelectedItem = true;
        if (change.Property != ItemsSourceProperty) return;
        if (source is not null) { source.BatchStarting -= Starting; source.BatchCompleted -= Completed; }
        source = ItemsSource as StatusList;
        if (source is not null) { source.BatchStarting += Starting; source.BatchCompleted += Completed; }
    }
    private void Starting(object? sender, EventArgs e)
    {
        AutoScrollToSelectedItem = false;
        editing = true;
        selected = SelectedItem as StatusRow;
        rows = [.. SelectedItems!.OfType<StatusRow>()];
        scroller = this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var first = GetRealizedContainers().OfType<ListBoxItem>().FirstOrDefault();
        anchor = first?.DataContext as StatusRow;
        anchorIndex = anchor is null ? -1 : source!.IndexOf(anchor);
        rowHeight = first?.Bounds.Height ?? 0;
        offset = scroller?.Offset ?? default;
    }
    private void Completed(object? sender, EventArgs e)
    {
        try
        {
            var index = anchor is null ? -1 : source!.IndexOf(anchor);
            if (scroller is not null && anchorIndex >= 0 && index >= 0 && index != anchorIndex)
            {
                UpdateLayout();
                scroller.Offset = new(offset.X, Math.Max(0, offset.Y + (index - anchorIndex) * rowHeight));
            }
        }
        finally
        {
            if (rows.Any(row => source!.IndexOf(row) is >= 0 and var index && !Selection.IsSelected(index)))
                held = SelectedIndex == source!.IndexOf(selected) ? 1 : 2;
            if (held == 0) AutoScrollToSelectedItem = true;
            editing = false; anchor = null; scroller = null;
        }
    }
}
