using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Trag.Desktop.Composition;
using Trag.Desktop.Themes;
using Trag.Application.Live;
using Trag.Domain.Presentation;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Composition;

public sealed class ThemeControllerTests
{
    [Theory]
    [InlineData(ThemeChoice.System, "Default")] [InlineData(ThemeChoice.Light, "Light")] [InlineData(ThemeChoice.DarkEspresso, "DarkEspresso")] [InlineData(ThemeChoice.DarkGraphite, "Dark")] [InlineData(ThemeChoice.DarkObsidian, "DarkObsidian")]
    [InlineData(ThemeChoice.DarkVantablack, "DarkVantablack")]
    [InlineData((ThemeChoice)7, "Default")]
    public void WhenAThemeIsChosen_ShouldMapItToItsVariant(ThemeChoice theme, string expected) => Assert.Equal(expected, ThemeController.VariantFor(theme).ToString());

    [AvaloniaFact]
    public async Task WhenTheLayoutThemeChanges_ShouldSwitchTheApplicationTheme()
    {
        var application = Avalonia.Application.Current!;
        var layout = new LayoutPreferences(new FakeLayoutStore(), new StatusBarState());
        try
        {
            ThemeController.Follow(application, layout);
            layout.ThemeIndex = 5; Assert.Same(AppThemes.DarkVantablack, application.RequestedThemeVariant);
            layout.ThemeIndex = 4; Assert.Same(AppThemes.DarkObsidian, application.RequestedThemeVariant);
            layout.ThemeIndex = 3; Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);
            layout.ThemeIndex = 2; Assert.Same(AppThemes.DarkEspresso, application.RequestedThemeVariant);
            layout.ThemeIndex = 1; Assert.Equal(ThemeVariant.Light, application.RequestedThemeVariant);
            layout.ThemeIndex = 0; Assert.Equal(ThemeVariant.Default, application.RequestedThemeVariant);
        }
        finally { application.RequestedThemeVariant = ThemeVariant.Default; await layout.SaveCompletion; }
    }
}
