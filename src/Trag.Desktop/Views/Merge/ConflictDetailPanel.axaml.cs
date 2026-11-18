using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Trag.Desktop.Views.Merge;

public sealed partial class ConflictDetailPanel : UserControl
{
    // The width the actions over a block may take: what shows, less the card's margin and border and the padding of its
    // header (Themes/Merge.axaml).
    public static readonly IValueConverter InsideCard = new FuncValueConverter<double, double>(viewport => viewport - 54);

    public ConflictDetailPanel() => InitializeComponent();
}
