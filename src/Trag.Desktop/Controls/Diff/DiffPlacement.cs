namespace Trag.Desktop.Controls.Diff;

// What to show: the vertical offset of the rows and the selected unified line. The start is the top with no selection.
internal readonly record struct DiffPlacement(double VerticalOffset, int Selected)
{
    public static DiffPlacement Start => new(0, -1);
}
