using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Trag.ViewModels.Rebase;

namespace Trag.Desktop.Dialogs;

// The rows move by keyboard and by mouse. Alt and an arrow move the chosen commit, before the list takes the arrow to
// move the selection. A press on a row's handle drags its commit: the list holds the pointer, since a moved row may get
// another container, and the commit takes the place of the row under the pointer until it is let go.
public sealed partial class RewriteDialog : DialogHost
{
    private RewriteRow? dragged;

    public RewriteDialog()
    {
        InitializeComponent();
        RewriteRows.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.Alt || DataContext is not RewriteViewModel rewrite) return;
            var move = e.Key switch { Key.Up => rewrite.MoveUpCommand, Key.Down => rewrite.MoveDownCommand, _ => null };
            if (move is null) return;
            if (move.CanExecute(null)) move.Execute(null);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        RewriteRows.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is not Control source
                || !source.GetSelfAndVisualAncestors().OfType<Control>().Any(control => control.Classes.Contains("grip"))
                || source.DataContext is not RewriteRow row || DataContext is not RewriteViewModel rewrite) return;
            dragged = row;
            rewrite.SelectedRow = row;
            e.Pointer.Capture(RewriteRows);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        RewriteRows.PointerMoved += (_, e) =>
        {
            if (dragged is null || DataContext is not RewriteViewModel rewrite) return;
            var place = PlaceAt(e.GetPosition(RewriteRows).Y);
            if (place != rewrite.Rows.IndexOf(dragged)) rewrite.MoveTo(dragged, place);
        };
        // Letting go ends the list's hold on the pointer, and losing that hold, however it goes, ends the drag.
        RewriteRows.PointerCaptureLost += (_, _) => dragged = null;
    }

    // The place of the row under the height: the first whose bottom lies below it, or the last one the list has made, since
    // a long list makes rows only around what it shows.
    private int PlaceAt(double y)
    {
        var last = 0;
        for (var index = 0; index < RewriteRows.ItemCount; index++)
            if (RewriteRows.ContainerFromIndex(index) is { } row && row.TranslatePoint(default, RewriteRows) is { } top)
            {
                if (y < top.Y + row.Bounds.Height) return index;
                last = index;
            }
        return last;
    }
}
