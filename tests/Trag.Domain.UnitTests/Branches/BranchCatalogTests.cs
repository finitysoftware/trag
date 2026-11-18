using Trag.Domain.Branches;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Branches;

public sealed class BranchCatalogTests
{
    [Theory]
    [InlineData("", "main", "feature/One,fix/Two", "origin/main,origin/One,upstream/Two")]
    [InlineData("   ", "main", "feature/One,fix/Two", "origin/main,origin/One,upstream/Two")]
    [InlineData("  ONE  ", null, "feature/One", "origin/One")]
    [InlineData("mAiN", "main", "", "origin/main")]
    [InlineData("Two", null, "fix/Two", "upstream/Two")]
    [InlineData("missing", null, "", "")]
    public void WhenTheBranchesAreFiltered_ShouldKeepTheNamesThatMatchInAnyCaseAndLeaveTheCatalogWhole(string query, string? current, string local, string remote)
    {
        var set = new BranchSet(
            [Local("fix/Two"), Local("main"), Local("feature/One")],
            [Remote("upstream/Two", "upstream"), Remote("origin/One", "origin"), Remote("origin/main", "origin")], "main");
        var all = BranchCatalog.Create(set, Snapshots.Create(branch: null));

        var visible = all.Filter(query);

        Assert.Equal(current, visible.Current?.Name);
        Assert.Equal(local, string.Join(',', visible.Local.Select(branch => branch.Name)));
        Assert.Equal(remote, string.Join(',', visible.Remote.Select(branch => branch.Name)));
        Assert.All(visible.RemoteGroups, group => Assert.NotEmpty(group.Branches));
        Assert.Equal(2, all.Local.Count);
        Assert.Equal(3, all.Remote.Count());
        Assert.Equal("main", all.Current!.Name);
        Assert.Same(all, all.Filter(" "));
        Assert.Equal(["main", "feature/One", "fix/Two"], all.LocalNames);
    }

    [Fact]
    public void WhenRemotesDifferOnlyInCase_ShouldKeepThemApartAsGroupsInGitsOrder()
    {
        var all = CaseDistinctRemotes();
        Assert.Equal(["", "origin", "Origin", "upstream"], all.RemoteGroups.Select(group => group.Remote));
    }

    [Fact]
    public void WhenGitListedMoreBranchesThanWereRead_ShouldSayTheCatalogIsTruncated()
    {
        var all = CaseDistinctRemotes();
        Assert.True(all.Truncated);
    }

    [Fact]
    public void WhenRemoteBranchesAreFiltered_ShouldDropTheEmptyGroupsAndKeepEqualNamesInGitsOrder()
    {
        var filtered = CaseDistinctRemotes().Filter("keep");
        Assert.Equal(["origin", "Origin", "upstream"], filtered.RemoteGroups.Select(group => group.Remote));
        Assert.Equal(["origin/keep", "origin/KEEP"], filtered.RemoteGroups[0].Branches.Select(branch => branch.Name));
    }

    [Fact]
    public void WhenATruncatedCatalogIsFiltered_ShouldStayTruncated()
    {
        var filtered = CaseDistinctRemotes().Filter("keep");
        Assert.True(filtered.Truncated);
    }

    [Fact]
    public void WhenNoBranchesWereRead_ShouldHaveNoCurrentBranch()
    {
        Assert.Null(BranchCatalog.Empty.Current);
        Assert.Empty(BranchCatalog.Empty.LocalNames);
    }

    [Fact]
    public void WhenHeadIsDetached_ShouldListEveryLocalBranchAndNoneAsCurrent()
    {
        var detached = BranchCatalog.Create(new([Local("topic")], [], null), Snapshots.Create(branch: null));
        Assert.Null(detached.Current);
        Assert.Equal("topic", Assert.Single(detached.Local).Name);
    }

    [Fact]
    public void WhenTheBranchReadNamesNoCurrentBranch_ShouldTakeTheOneTheSnapshotNames()
    {
        var fallback = BranchCatalog.Create(new([Local("topic")], [], null), Snapshots.Create(branch: "topic"));
        Assert.Equal("topic", fallback.Current!.Name);
        Assert.Empty(fallback.Local);
    }

    [Fact]
    public void WhenARemoteBranchIsNamedAfterItsRemote_ShouldKnowItsNameOnTheRemote()
    {
        Assert.Equal("feature", Remote("origin/feature", "origin").LocalName);
    }

    [Fact]
    public void WhenNothingFollowsTheRemotesName_ShouldKeepTheWholeName()
    {
        Assert.Equal("origin/", Remote("origin/", "origin").LocalName);
    }

    [Fact]
    public void WhenTheBranchIsLocal_ShouldBeItsOwnLocalName()
    {
        Assert.Equal("topic", Local("topic").LocalName);
    }

    [Fact]
    public void WhenARemoteBranchHasNoRemote_ShouldKeepItsName()
    {
        Assert.Equal("orphan", Remote("orphan", null).LocalName);
    }

    // A remote may be named with a slash. Its branches go under the longest configured remote their name starts with, so
    // team/origin/main is main on team/origin, not origin/main on team; a ref of a remote no longer configured keeps the
    // remote its name begins with.
    [Fact]
    public void WhenARemoteNameContainsASlash_ShouldGroupByTheWholeRemoteName()
    {
        var set = new BranchSet([], [Remote("team/origin/main", "team"), Remote("team/feature", "team"), Remote("gone/topic", "gone")], null);
        var remotes = new RepositoryRemote[] { new("team", "https://example.invalid/team.git", "https://example.invalid/team.git"),
            new("team/origin", "https://example.invalid/origin.git", "https://example.invalid/origin.git") };

        var catalog = BranchCatalog.Create(set, Snapshots.Create(remotes: remotes));

        Assert.Equal(["gone: topic", "team: feature", "team/origin: main"],
            catalog.RemoteGroups.SelectMany(group => group.Branches.Select(branch => group.Remote + ": " + branch.LocalName)));
        Assert.All(catalog.RemoteGroups, group => Assert.All(group.Branches, branch => Assert.Equal(group.Remote, branch.Remote)));
    }

    private static BranchInfo Local(string name) => new(name, "refs/heads/" + name, Snapshots.HeadId, false);
    private static BranchInfo Remote(string name, string? remote) => new(name, "refs/remotes/" + name, Snapshots.HeadId, true, remote);

    [Fact]
    public void WhenOtherLocalBranchesMatch_ShouldMakeTheFirstOneWhatEnterSwitchesTo()
    {
        var catalog = MainTopicAndOriginTopic();
        Assert.Equal("topic", catalog.FirstMatch?.Name);
    }

    [Fact]
    public void WhenNoOtherLocalBranchMatches_ShouldMakeTheFirstRemoteOneWhatEnterSwitchesTo()
    {
        var catalog = MainTopicAndOriginTopic();
        Assert.Equal("origin/topic", (catalog with { Local = [] }).FirstMatch?.Name);
    }

    [Fact]
    public void WhenOnlyTheCurrentBranchMatches_ShouldMakeItWhatEnterSwitchesTo()
    {
        var catalog = MainTopicAndOriginTopic();
        Assert.Equal("main", catalog.Filter("mai").FirstMatch?.Name);
    }

    [Fact]
    public void WhenTheCatalogIsEmpty_ShouldGiveEnterNothingToSwitchTo()
    {
        Assert.Null(BranchCatalog.Empty.FirstMatch);
    }

    // Remote groups that differ only in case, equal names in Git's order, a branch with no remote, and more branches than
    // were read.
    private static BranchCatalog CaseDistinctRemotes()
    {
        var set = new BranchSet([], [
            Remote("origin/remove", "origin"),
            Remote("upstream/keep", "upstream"),
            Remote("Origin/keep", "Origin"),
            Remote("origin/keep", "origin"),
            Remote("origin/KEEP", "origin"),
            Remote("orphan", null)
        ], null) { Truncated = true };
        return BranchCatalog.Create(set, Snapshots.Create(branch: null));
    }

    private static BranchCatalog MainTopicAndOriginTopic() =>
        BranchCatalog.Create(new([new("main", "refs/heads/main", "1", false), new("topic", "refs/heads/topic", "2", false)],
            [new("origin/topic", "refs/remotes/origin/topic", "2", true, "origin")], "main"), Snapshots.Create());
}
