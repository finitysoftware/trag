using System.Xml.Linq;

namespace Trag.ArchitectureTests;

// Trag's own license, the LICENSE file at the repository's root, ships beside the executable as LICENSE.txt, which the
// program the system has for text opens; the SDK publishes a file the build copies unless the item says otherwise.
public sealed class LicenseTests
{
    [Fact]
    public void TheLicenseIsPublishedBesideTheExecutableAsText()
    {
        var project = RepositoryRoot.Project("Trag.Desktop");
        var license = Path.Combine(RepositoryRoot.Path, "LICENSE");
        var entry = XDocument.Load(Path.Combine(project, "Trag.Desktop.csproj")).Descendants("None")
            .Single(item => Path.GetFullPath(Path.Combine(project, (string)item.Attribute("Include")!)) == license);

        Assert.Equal("LICENSE.txt", (string?)entry.Attribute("Link"));
        Assert.Contains((string?)(entry.Attribute("CopyToPublishDirectory") ?? entry.Attribute("CopyToOutputDirectory")), new string?[] { "Always", "PreserveNewest", "IfDifferent" });
    }
}
