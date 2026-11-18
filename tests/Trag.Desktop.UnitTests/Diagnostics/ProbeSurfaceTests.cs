using System.Text.RegularExpressions;
using Trag.Desktop.Diagnostics.Probe;

namespace Trag.Desktop.UnitTests.Diagnostics;

public sealed class ProbeSurfaceTests
{
    // Renaming a control in XAML must not leave the probe (and with it the smokes) reaching for a name that is gone.
    [Fact]
    public void WhenTheProbeReachesForAControlName_ShouldFindItDeclaredInTheXaml()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Trag.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var declared = Directory.EnumerateFiles(Path.Combine(root!.FullName, "src", "Trag.Desktop"), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "\\bName=\"([A-Za-z]+)\"").Select(match => match.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(declared);
        Assert.All(ProbeSurface.Names, name => Assert.Contains(name, declared));
    }
}
