using Avalonia.Controls;
using Avalonia.Data.Converters;
using Trag.Domain.Blame;

namespace Trag.Desktop.Views.History;

public sealed partial class BlameView : UserControl
{
    // The oldest commit's stripe is faint, the newest one's full.
    public static readonly IValueConverter AgeOpacity = new FuncValueConverter<int, double>(age => 0.15 + 0.85 * age / BlameRows.Newest);

    public BlameView() => InitializeComponent();
}
