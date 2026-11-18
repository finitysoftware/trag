using System.Text.Json;
using System.Xml.Linq;

namespace Trag.ArchitectureTests;

// What ships with Trag is named in the third-party notices that ship beside it: every package whose files reach the build
// of Trag.Desktop, as the restore recorded it, appears on a "Packages:" line, and the notices go out beside the executable.
public sealed class ThirdPartyNoticesTests
{
    [Fact]
    public void EveryPackageThatShipsIsNamedInTheThirdPartyNotices()
    {
        using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Project("Trag.Desktop"), "obj", "project.assets.json")));
        var shipped = assets.RootElement.GetProperty("targets").EnumerateObject()
            .SelectMany(target => target.Value.EnumerateObject())
            .Where(library => library.Value.GetProperty("type").GetString() == "package" && Ships(library.Value))
            .Select(library => library.Name[..library.Name.IndexOf('/')]).Distinct();
        var named = File.ReadLines(Path.Combine(RepositoryRoot.Path, "docs", "THIRD-PARTY-NOTICES.txt"))
            .Where(line => line.StartsWith("Packages: ", StringComparison.Ordinal))
            .SelectMany(line => line["Packages: ".Length..].Split(", ")).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(shipped.Where(package => !named.Contains(package)).Order(StringComparer.Ordinal));
    }

    // A package ships when it gives the build a runtime or a native file; "_._" stands for none.
    private static bool Ships(JsonElement library) =>
        new[] { "runtime", "native", "runtimeTargets" }.Any(kind => library.TryGetProperty(kind, out var files)
            && files.EnumerateObject().Any(file => !file.Name.EndsWith("_._", StringComparison.Ordinal)));

    // The SDK publishes a file the build copies unless the item says otherwise, and puts a file from outside the project
    // at the root of the output under its own name unless a Link places it elsewhere.
    [Fact]
    public void TheThirdPartyNoticesArePublishedBesideTheExecutable()
    {
        var project = RepositoryRoot.Project("Trag.Desktop");
        var notices = Path.Combine(RepositoryRoot.Path, "docs", "THIRD-PARTY-NOTICES.txt");
        var entry = XDocument.Load(Path.Combine(project, "Trag.Desktop.csproj")).Descendants("None")
            .Single(item => Path.GetFullPath(Path.Combine(project, (string)item.Attribute("Include")!)) == notices);

        Assert.Equal("THIRD-PARTY-NOTICES.txt", (string?)entry.Attribute("Link") ?? Path.GetFileName(notices));
        Assert.Contains((string?)(entry.Attribute("CopyToPublishDirectory") ?? entry.Attribute("CopyToOutputDirectory")), new string?[] { "Always", "PreserveNewest", "IfDifferent" });
    }
}
