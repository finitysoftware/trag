using Avalonia.Controls;

namespace Trag.Desktop.Views.Rebase;

public sealed partial class RebaseView : UserControl
{
    public RebaseView()
    {
        InitializeComponent();
        // The plan takes what it needs up to about a third of the column, so a long rebase still leaves the conflicts room.
        RebaseFilesColumn.SizeChanged += (_, e) => RebaseStepsScroller.MaxHeight = Math.Max(60, e.NewSize.Height * 0.35);
    }
}
