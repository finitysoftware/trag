using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.Presentation.Text;

// The version line on the About page in Settings.
public sealed class AboutTextTests
{
    // What the build appends after "+" names the commit it was built from; the version is what comes before it.
    [Theory]
    [InlineData("1.0.0", "Version 1.0.0")]
    [InlineData("1.2.0-beta.1+6a8ecfbd0c2e", "Version 1.2.0-beta.1")]
    public void WhenTheBuildCarriesAVersion_ShouldShowItWithoutTheBuildMetadata(string informational, string shown) =>
        Assert.Equal(shown, AboutText.Version(informational));
}
