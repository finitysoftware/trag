using Trag.Desktop.Diagnostics.Probe.Commands;
using Trag.Domain.Presentation;

namespace Trag.Desktop.UnitTests.Diagnostics;

public sealed class LifecycleCommandsTests
{
    [Fact]
    public void WhenThemeDarkIsAsked_ShouldChooseANamedDarkTheme() =>
        Assert.Equal(ThemeChoice.DarkGraphite, LifecycleCommands.Theme("dark"));

    [Theory]
    [InlineData("light", ThemeChoice.Light)]
    [InlineData("system", ThemeChoice.System)]
    [InlineData("DarkObsidian", ThemeChoice.DarkObsidian)]
    public void WhenAThemeIsAskedByItsOwnName_ShouldChooseThatTheme(string name, ThemeChoice theme) =>
        Assert.Equal(theme, LifecycleCommands.Theme(name));

    [Fact]
    public void WhenAnUnknownThemeIsAsked_ShouldRefuseItInsteadOfChoosingTheSystemTheme() =>
        Assert.Throws<ArgumentException>(() => LifecycleCommands.Theme("sepia"));
}
