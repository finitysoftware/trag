using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Trag.Desktop.Controls;
using Trag.Domain.Presentation;

namespace Trag.Desktop.UnitTests.Controls;

// Every kind of token has a color of its own in both themes, so no kind the scanner finds is drawn without one or in
// another kind's color.
public sealed class SyntaxColorsTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WhenAThemeColorsCode_ShouldHaveADistinctColorForEveryKindOfToken(bool dark)
    {
        var colors = SyntaxColors.For(dark).Select(brush => ((ISolidColorBrush)brush).Color).ToArray();

        Assert.Equal(Enum.GetValues<SyntaxKind>().Length, colors.Length);
        Assert.Equal(colors.Length, colors.Distinct().Count());
    }

    // The colors are made once for the process, on whichever thread asks first; an immutable brush belongs to none, so a
    // test or a background build that asks first cannot tie them to its thread.
    [Fact]
    public void WhenTheColorsAreFirstAskedForOffTheUiThread_ShouldBeBrushesNoThreadOwns() =>
        Assert.All(SyntaxColors.For(true).Concat(SyntaxColors.For(false)), brush => Assert.IsAssignableFrom<IImmutableBrush>(brush));
}
