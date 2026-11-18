using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class RepositoryHeaderTests
{
    [Fact]
    public void WhenARepositoryIsShown_ShouldNameTheRepositoryTheBranchAndTheGitVersion()
    {
        var header = RepositoryHeader.From(Snapshots.Create(root: Path.Combine("repos", "trag")));
        Assert.Equal("trag", header.RepositoryName);
        Assert.Equal("main", header.BranchText);
        Assert.Equal("Git 2.43.0", header.GitVersionText); Assert.Equal("", header.Warning);
    }

    [Fact]
    public void WhenTheBranchHasNoCommitsYet_ShouldSaySo() =>
        Assert.Equal("main · no commits yet", RepositoryHeader.From(Snapshots.Create(unborn: true)).BranchText);

    // A submodule offers the way back to the repository it belongs to, by that repository's name.
    [Fact]
    public void WhenTheRepositoryIsASubmodule_ShouldOfferTheWayBackToItsSuperproject()
    {
        Assert.Equal("Back to super", RepositoryHeader.From(Snapshots.Create(Path.Combine("work", "super", "sub")) with { Superproject = Path.Combine("work", "super") }).Superproject);
        Assert.Equal("", RepositoryHeader.From(Snapshots.Create()).Superproject);
    }

    [Fact]
    public void WhenNoRepositoryIsOpen_ShouldSaySo() => Assert.Equal("No repository open", RepositoryHeader.Empty.RepositoryName);

    [Fact]
    public void WhenHeadIsDetached_ShouldWarnAndCountTheCommitsOnNoBranch()
    {
        var detached = RepositoryHeader.From(Snapshots.Create(branch: null, detachedCommits: 2));
        Assert.Equal("Detached at aaaaaaa", detached.BranchText);
        Assert.Equal("Detached HEAD at aaaaaaa. 2 commits are on no branch yet; create a branch to keep them.", detached.Warning);
    }

    [Fact]
    public void WhenAnOperationTragDoesNotHandleIsInProgress_ShouldWarnToFinishItInGitToolsAndListTheLimitations()
    {
        var picking = RepositoryHeader.From(Snapshots.Create(operation: RepositoryOperation.CherryPick, limitations: ["Sparse checkout"]));
        Assert.Equal("Cherry-pick in progress. Resolve or finish it in your Git tools, then refresh.\nSparse checkout", picking.Warning);
    }

    [Fact]
    public void WhenADetachedHeadHoldsNoCommitOfItsOwn_ShouldSayItIsNotOnABranch()
    {
        Assert.Equal("Not on a branch", new DetachedHead(Snapshots.HeadId, 0).CardText);
        Assert.Equal("Detached HEAD at aaaaaaa. Commits made here are on no branch until you create one.", new DetachedHead(Snapshots.HeadId, 0).Warning);
    }

    [Fact]
    public void WhenADetachedHeadHoldsOneCommit_ShouldCountItInTheSingular() =>
        Assert.Equal("1 commit is not on a branch", new DetachedHead(Snapshots.HeadId, 1).CardText);

    [Fact]
    public void WhenADetachedHeadHoldsSeveralCommits_ShouldAskToKeepThemAll() =>
        Assert.Equal("Keep 3 commits?", new DetachedHead(Snapshots.HeadId, 3).KeepTitle);
}
