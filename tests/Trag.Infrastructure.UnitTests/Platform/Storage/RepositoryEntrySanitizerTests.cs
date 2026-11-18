using Trag.Domain.Catalog;
using Trag.Infrastructure.Platform.Storage;
using Trag.Domain.Paths;

namespace Trag.Infrastructure.UnitTests.Platform.Storage;

// What the catalog keeps from a list of links, whether it came from disk or from the shell.
public sealed class RepositoryEntrySanitizerTests
{
    private static readonly DateTimeOffset When = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);
    private static RepositoryEntry Entry(string path, string name = "name", string? remote = null, string? branch = null) =>
        new(path, name, When, When, remote, branch);

    private static RepositoryEntry[] Named() =>
        RepositoryEntrySanitizer.Sanitize([Entry(Path.Combine("x", "repo"), ""), Entry(Path.Combine("x", "other"), "  Other  ")]);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has\u0001control")]
    public void WhenAPathCannotBeOpened_ShouldDropTheEntry(string path) =>
        Assert.Empty(RepositoryEntrySanitizer.Sanitize([Entry(path)]));

    [Fact]
    public void WhenAPathIsAtTheLengthLimit_ShouldKeepTheEntry() =>
        Assert.Single(RepositoryEntrySanitizer.Sanitize([Entry(new string('a', 4096))]));

    [Fact]
    public void WhenAPathIsLongerThanTheLimit_ShouldDropTheEntry() =>
        Assert.Empty(RepositoryEntrySanitizer.Sanitize([Entry(new string('a', 4097))]));

    [Fact]
    public void WhenAnEntryHasNoName_ShouldNameItAfterItsFolder()
    {
        var entries = Named();
        Assert.Equal("repo", entries[0].Name);
    }

    [Fact]
    public void WhenAnEntryHasAName_ShouldTrimIt()
    {
        var entries = Named();
        Assert.Equal("Other", entries[1].Name);
    }

    [Fact]
    public void WhenTheAddressAndTheBranchAreTooLongToBeOne_ShouldForgetThem()
    {
        var entry = Assert.Single(RepositoryEntrySanitizer.Sanitize(
            [Entry("repo", remote: new string('u', 2049), branch: new string('b', 513))]));
        Assert.Null(entry.RemoteUrl);
        Assert.Null(entry.Branch);
    }

    [Fact]
    public void WhenTheAddressAndTheBranchAreAtTheirLimits_ShouldKeepThem()
    {
        var kept = Assert.Single(RepositoryEntrySanitizer.Sanitize(
            [Entry("repo", remote: new string('u', 2048), branch: new string('b', 512))]));
        Assert.NotNull(kept.RemoteUrl);
        Assert.NotNull(kept.Branch);
    }

    [Fact]
    public void WhenTheSamePathComesTwice_ShouldKeepTheFirstAsTheOneLink()
    {
        var trag = Path.Combine(Path.GetFullPath("catalog-root"), "Trag");
        var entries = RepositoryEntrySanitizer.Sanitize([Entry(trag, "First"), Entry(trag + Path.DirectorySeparatorChar, "Second")]);
        Assert.Equal("First", Assert.Single(entries).Name);
    }

    // What is saved keeps one entry for each link the catalog's list finds: the list matches by the full path, so a
    // parent step names the same folder, and it compares names as the file system does.
    [Fact]
    public void WhenTwoPathsNameTheSameFolderThroughAParentStep_ShouldKeepTheFirstAsTheOneLink()
    {
        var root = Path.GetFullPath("catalog-root");
        var entries = RepositoryEntrySanitizer.Sanitize([Entry(Path.Combine(root, "Trag"), "First"), Entry(Path.Combine(root, "x", "..", "Trag"), "Second")]);
        Assert.Equal("First", Assert.Single(entries).Name);
    }

    [Fact]
    public void WhenTwoPathsDifferOnlyInCase_ShouldBeOneLinkExactlyWhenTheListFindsThemAsOne()
    {
        var root = Path.GetFullPath("catalog-root");
        var (lower, upper) = (Path.Combine(root, "trag"), Path.Combine(root, "TRAG"));
        var one = LocalPaths.Comparer.Equals(LocalPaths.Key(lower), LocalPaths.Key(upper));
        Assert.Equal(one ? 1 : 2, RepositoryEntrySanitizer.Sanitize([Entry(lower), Entry(upper)]).Length);
    }

    [Fact]
    public void WhenThereAreMoreEntriesThanTheListMayHold_ShouldKeepNoMoreThanItMay()
    {
        var many = Enumerable.Range(0, RepositoryEntry.MaximumListed + 10).Select(i => Entry("repo-" + i)).ToArray();
        Assert.Equal(RepositoryEntry.MaximumListed, RepositoryEntrySanitizer.Sanitize(many).Length);
    }
}
