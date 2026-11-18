using System.Reflection;

namespace Trag.Desktop.UnitTests.Platform;

// The program is Trag.exe, Trag - Git Client by Finity Software: the names Windows shows in the file's details, the task
// manager and the installer.
public sealed class ProductIdentityTests
{
    private static readonly Assembly Trag = typeof(App).Assembly;

    [Fact]
    public void WhenTragIsBuilt_ShouldBeNamedTrag() => Assert.Equal("Trag", Trag.GetName().Name);

    [Fact]
    public void WhenWindowsDescribesTheProgram_ShouldNameTragGitClientByFinitySoftware() =>
        Assert.Equal(("Trag", "Trag - Git Client", "Finity Software", "© 2026 Finity Software"),
            (Trag.GetCustomAttribute<AssemblyProductAttribute>()?.Product, Trag.GetCustomAttribute<AssemblyTitleAttribute>()?.Title,
                Trag.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company, Trag.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright));
}
