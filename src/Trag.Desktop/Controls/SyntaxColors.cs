using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Trag.Desktop.Controls;

// The colors of code, one for each kind of token, in the order of SyntaxKind (keyword, string, comment, number, type,
// method, member, tag), for the dark and the light theme, after the IDE's own: the diff and the blame color code alike.
public static class SyntaxColors
{
    private static readonly IBrush[] Dark = BrushesFor("#6C95EB", "#C9A26D", "#85C46C", "#ED94C0", "#C191FF", "#39CC9B", "#66C3CC", "#E8BF6A");
    private static readonly IBrush[] Light = BrushesFor("#0033B3", "#067D17", "#8C8C8C", "#1750EB", "#7B2FBE", "#00627A", "#871094", "#9E6A03");

    public static IBrush[] For(bool dark) => dark ? Dark : Light;

    // Immutable, so whichever thread asks first ties them to none.
    private static IBrush[] BrushesFor(params string[] values) => values.Select(value => (IBrush)new ImmutableSolidColorBrush(Color.Parse(value))).ToArray();
}
