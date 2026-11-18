using Avalonia.Controls;
using Trag.Desktop.Controls;

namespace Trag.Desktop.Views.History;

public sealed partial class CommitDetailsPanel : UserControl
{
    public CommitDetailsPanel()
    {
        InitializeComponent();
        CommitFilesList.ContextRequested += RowMenu.Open;
    }
}
