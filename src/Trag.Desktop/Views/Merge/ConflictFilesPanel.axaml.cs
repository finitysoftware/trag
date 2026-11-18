using Avalonia.Controls;

namespace Trag.Desktop.Views.Merge;

public sealed partial class ConflictFilesPanel : UserControl
{
    public ConflictFilesPanel()
    {
        InitializeComponent();
        // The conflicts take what they need up to a little over half the column, so the list stays virtualized with
        // thousands of them and the decided files below keep their room.
        ConflictFilesColumn.SizeChanged += (_, e) => ConflictList.MaxHeight = Math.Max(48, e.NewSize.Height * 0.55);
    }
}
