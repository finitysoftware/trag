using System.Text;
using Trag.Domain.History;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

// The names Git prints for one commit (git log --decorate=full, %D) as the history shows them beside the message.
public sealed class CommitRefParserTests
{
    private static IReadOnlyList<CommitRef> Parse(string decorations) => CommitRefParser.Parse(Encoding.UTF8.GetBytes(decorations));

    [Fact]
    public void WhenACommitCarriesNamesOfEveryKind_ShouldPutHeadsBranchFirstThenLocalRemoteAndTagsAndLeaveOutGitsPrivateRefs()
    {
        var refs = Parse("tag: refs/tags/v1.0, refs/remotes/origin/main, HEAD -> refs/heads/main, refs/remotes/origin/HEAD, refs/stash, refs/heads/feature/x");
        Assert.Equal([("main", CommitRefKind.CurrentBranch), ("feature/x", CommitRefKind.Branch), ("origin/main", CommitRefKind.RemoteBranch),
            ("v1.0", CommitRefKind.Tag)], refs.Select(name => (name.Name, name.Kind)));
        Assert.True(refs[0].IsCurrent && refs[0].IsBranch); Assert.True(refs[2].IsRemote); Assert.True(refs[3].IsTag);
    }

    // The full ref name tells a local branch from a remote one of the same short name.
    [Fact]
    public void WhenALocalBranchIsNamedLikeARemoteOne_ShouldKeepItLocal()
    {
        var refs = Parse("refs/heads/origin/main, refs/remotes/origin/main");
        Assert.Equal([CommitRefKind.Branch, CommitRefKind.RemoteBranch], refs.Select(name => name.Kind));
        Assert.All(refs, name => Assert.Equal("origin/main", name.Name));
    }

    // A tag and a branch of one name on one commit are two names: each keeps its badge.
    [Fact]
    public void WhenATagAndABranchShareAName_ShouldKeepBothBadges()
    {
        var refs = Parse("tag: refs/tags/release, refs/heads/release");
        Assert.Equal([("release", CommitRefKind.Branch), ("release", CommitRefKind.Tag)], refs.Select(name => (name.Name, name.Kind)));
    }

    [Fact]
    public void WhenHeadIsDetached_ShouldListItFirstAsANameOfItsOwn()
    {
        var refs = Parse("refs/heads/topic, HEAD");
        Assert.Equal([("HEAD", CommitRefKind.DetachedHead), ("topic", CommitRefKind.Branch)], refs.Select(name => (name.Name, name.Kind)));
        Assert.True(refs[0].IsHead);
    }

    [Fact]
    public void WhenACommitCarriesNoName_ShouldListNothing() => Assert.Empty(Parse(""));

    [Fact]
    public void WhenACommitCarriesOnlyGitsPrivateRefs_ShouldListNothing() => Assert.Empty(Parse("refs/notes/commits, refs/bisect/bad"));

    // A name that is only its prefix names nothing, so it gets no badge.
    [Fact]
    public void WhenANameIsOnlyItsPrefix_ShouldLeaveItOut() => Assert.Empty(Parse("HEAD -> refs/heads/, refs/remotes/, tag: refs/tags/, refs/heads/"));

    [Fact]
    public void WhenANameHasLettersOutsideAscii_ShouldKeepThem() => Assert.Equal("čćž/grana", Assert.Single(Parse("refs/heads/čćž/grana")).Name);

    [Fact]
    public void WhenANameIsTooLong_ShouldClipIt() => Assert.True(Assert.Single(Parse("refs/heads/" + new string('n', 400))).Name.Length <= 257);

    [Fact]
    public void WhenACommitCarriesTooManyNames_ShouldListNoMoreThan32() =>
        Assert.Equal(32, Parse(string.Join(", ", Enumerable.Range(0, 50).Select(i => "refs/heads/b" + i))).Count);
}
