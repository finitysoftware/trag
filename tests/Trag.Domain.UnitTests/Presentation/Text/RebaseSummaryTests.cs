using Trag.Domain.Branches;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Presentation.Text;

// What the rebase side of the merge dialog says for each kind of rebase: commits to replay onto a branch that moved on, a
// branch already on its remote, files changed on both sides, a branch that only moves, nothing to rebase, and uncommitted
// changes in the way.
public sealed class RebaseSummaryTests
{
    private static readonly BranchInfo Feature = new("feature", "refs/heads/feature", Snapshots.HeadId, false, null, "refs/remotes/origin/feature");
    private static readonly BranchInfo Main = new("main", "refs/heads/main", Snapshots.Id('b'), false);
    private static readonly RebaseChoice OntoMain = new(Main, Feature);
    private static readonly RebasePreview Replay = new(Snapshots.HeadId, Snapshots.Id('b'),
        [Snapshots.Commit(Snapshots.Id('c'), "Add login")], 7, 3, [], null, 0);

    [Fact]
    public void WhenABranchThatIsOnlyLocalIsRebased_ShouldReplayItsCommitsAndSayNoOneElseIsAffected()
    {
        var summary = new RebaseSummary(OntoMain, Replay, Snapshots.Create(branch: "feature"));
        Assert.Equal(("Checked out · 7 commits of its own", "3 commits feature doesn't have"), (summary.BranchHint, summary.OntoHint));
        Assert.True(summary.ShowsResult);
        Assert.Equal("7 commits will be replayed on top of main", summary.ResultTitle);
        Assert.StartsWith("Each commit of feature is applied again after main's newest commit.", summary.ResultText);
        Assert.Equal(("No file is changed on both sides", true, true), (summary.CheckText, summary.ShowsCheck, summary.IsCheckKnown));
        Assert.Equal(("COMMITS BEING REPLAYED · 7", "and 6 more"), (summary.CommitsCaption, summary.MoreCommits));
        Assert.True(summary.ShowsLocalOnly); Assert.False(summary.ShowsPublished);
        Assert.Equal("feature is not on a remote yet, so rewriting its commits affects no one else.", summary.LocalOnlyText);
        Assert.Equal("Rebase feature onto main", summary.Action);
        Assert.True(summary.CanStart);
    }

    [Fact]
    public void WhenTheBranchIsAlreadyOnItsRemote_ShouldWarnThatTheRemoteBranchHasToBeReplaced()
    {
        var summary = new RebaseSummary(OntoMain, Replay with { Upstream = "origin/feature", Published = 5 }, Snapshots.Create(branch: "feature", trackingRemote: "origin"));
        Assert.Equal("Checked out · 7 commits of its own · on origin", summary.BranchHint);
        Assert.True(summary.ShowsPublished); Assert.False(summary.ShowsLocalOnly);
        Assert.Equal("feature is already on origin", summary.PublishedTitle);
        Assert.Contains("will no longer match origin/feature", summary.PublishedText);
        Assert.Contains("Force push with lease", summary.PublishedText);
        Assert.True(summary.CanStart);
    }

    // A remote may be named with a slash; the branch is on the remote it tracks, not on the part before the first slash.
    [Fact]
    public void WhenTheRemoteNameHasASlash_ShouldNameTheWholeRemote()
    {
        var summary = new RebaseSummary(OntoMain, Replay with { Upstream = "team/origin/feature", Published = 5 },
            Snapshots.Create(branch: "feature", remotes: [Snapshots.Remote("team"), Snapshots.Remote("team/origin")], trackingRemote: "team/origin", upstream: "team/origin/feature"));

        Assert.Equal(("Checked out · 7 commits of its own · on team/origin", "feature is already on team/origin"), (summary.BranchHint, summary.PublishedTitle));
    }

    [Fact]
    public void WhenTheBranchTracksARemoteThatHasNoneOfItsCommits_ShouldShowNeitherTheRemoteWarningNorTheLocalNote()
    {
        var notPushedYet = new RebaseSummary(OntoMain, Replay with { Upstream = "origin/feature" }, Snapshots.Create(branch: "feature"));
        Assert.False(notPushedYet.ShowsPublished); Assert.False(notPushedYet.ShowsLocalOnly);
    }

    [Fact]
    public void WhenFilesAreChangedOnBothSides_ShouldListThemInsteadOfTheCheck()
    {
        var paths = Enumerable.Range(1, 8).Select(i => Snapshots.Path("src/file " + i + ".cs")).ToArray();
        var summary = new RebaseSummary(OntoMain, Replay with { BothSides = paths }, Snapshots.Create(branch: "feature"));
        Assert.True(summary.HasBothSides); Assert.False(summary.ShowsCheck);
        Assert.Equal("8 files are changed on both sides", summary.ConflictTitle);
        Assert.Equal(RebaseSummary.ListedFiles, summary.ConflictFiles.Count);
        Assert.Equal("and 2 more files", summary.ConflictMore);
        Assert.Contains("Rebase in progress", summary.ConflictText);
        Assert.True(summary.CanStart);
    }

    [Fact]
    public void WhenGitCannotTellWhichFilesChangedOnBothSides_ShouldSayConflictsShowWhileTheRebaseRuns()
    {
        var unknown = new RebaseSummary(OntoMain, Replay with { BothSides = null }, Snapshots.Create(branch: "feature"));
        Assert.Equal(("Conflicts show while the rebase runs", false), (unknown.CheckText, unknown.IsCheckKnown));
    }

    [Fact]
    public void WhenTheBranchHasNoCommitsOfItsOwn_ShouldSayItOnlyMovesToTheTarget()
    {
        var summary = new RebaseSummary(OntoMain, Replay with { Replayed = [], ReplayedCount = 0 }, Snapshots.Create(branch: "feature"));
        Assert.Equal("feature moves ahead to main", summary.ResultTitle);
        Assert.Equal("feature has no commits of its own, so it simply moves to main.", summary.ResultText);
        Assert.False(summary.ShowsCheck);
        Assert.True(summary.CanStart);
    }

    [Fact]
    public void WhenTheBranchAlreadyHasEveryCommitOfTheTarget_ShouldSayItIsUpToDateAndNotStart()
    {
        var upToDate = new RebaseSummary(OntoMain, Replay with { OntoCount = 0 }, Snapshots.Create(branch: "feature"));
        Assert.Equal(("Already up to date", false), (upToDate.ResultTitle, upToDate.ShowsResult));
        Assert.Equal("Nothing feature doesn't have", upToDate.OntoHint);
        Assert.Equal("Rebase", upToDate.Action);
        Assert.False(upToDate.CanStart);
    }

    [Fact]
    public void WhenThePreviewIsNotReadYet_ShouldShowNoHintAboutTheTargetAndNotStart()
    {
        var unread = new RebaseSummary(OntoMain, RebasePreview.None, Snapshots.Create(branch: "feature"));
        Assert.Equal(("Checked out", "", ""), (unread.BranchHint, unread.OntoHint, unread.ResultTitle));
        Assert.False(unread.CanStart);
    }

    [Fact]
    public void WhenABranchIsRebasedOntoItself_ShouldAskForAnotherBranchAndNotStart()
    {
        var itself = new RebaseSummary(new(Feature, Feature), Replay, Snapshots.Create(branch: "feature"));
        Assert.Equal("Choose a branch other than feature", itself.OntoHint);
        Assert.False(itself.CanStart);
    }

    [Fact]
    public void WhenGitLeavesOutMergeCommitsAndCommitsTheTargetHas_ShouldCountThemApartFromTheReplayedOnes()
    {
        var summary = LeavingOut();
        Assert.Equal("Checked out · 10 commits of its own", summary.BranchHint);
        Assert.Equal(("7 commits will be replayed on top of main", "COMMITS BEING REPLAYED · 7"), (summary.ResultTitle, summary.CommitsCaption));
        Assert.Equal("Git leaves out 2 merge commits (conflicts they resolved can come back) and 1 commit main already has.", summary.LeftOutText);
    }

    [Fact]
    public void WhenGitLeavesOutOneMergeCommit_ShouldSayTheConflictsItResolvedCanComeBack()
    {
        var summary = LeavingOut();
        Assert.Equal("Git leaves out 1 merge commit; conflicts they resolved can come back.", (summary with { Preview = Replay with { Merges = 1 } }).LeftOutText);
    }

    [Fact]
    public void WhenGitLeavesOutOnlyCommitsTheTargetHas_ShouldCountThem()
    {
        var summary = LeavingOut();
        Assert.Equal("Git leaves out 3 commits main already has.", (summary with { Preview = Replay with { AlreadyOnto = 3 } }).LeftOutText);
    }

    [Fact]
    public void WhenGitLeavesNothingOut_ShouldSayNothingAboutIt()
    {
        var summary = LeavingOut();
        Assert.Equal("", (summary with { Preview = Replay }).LeftOutText);
    }

    [Fact]
    public void WhenTheBranchOthersBuildOnIsRebased_ShouldWarnAndOfferToRebaseTheOtherBranchOntoItInstead()
    {
        var (_, _, _, summary) = Mainline();
        Assert.True(summary.ShowsMainline); Assert.False(summary.ShowsPublished);
        Assert.Equal("This rewrites main, the branch others build on", summary.MainlineTitle);
        Assert.Equal("Its 7 commits get new ids on origin, and everyone who pulled main has to reset it. To bring grpc up to date instead, rebase it onto main.", summary.MainlineText);
        Assert.Equal((true, "Rebase grpc onto main instead"), (summary.CanRebaseOtherWay, summary.OtherWayAction));
    }

    [Fact]
    public void WhenTheBranchOthersBuildOnHasOneCommitToReplay_ShouldCountItInTheSingular()
    {
        var (_, _, pushed, summary) = Mainline();
        var one = summary with { Preview = pushed with { ReplayedCount = 1 } };
        Assert.StartsWith("Its 1 commit gets a new id on origin, and everyone who pulled main has to reset it.", one.MainlineText);
    }

    // A remote branch cannot be rebased.
    [Fact]
    public void WhenTheOtherBranchIsRemote_ShouldSayToSwitchToItFirstAndOfferNoButton()
    {
        var (main, _, _, summary) = Mainline();
        var remote = summary with { Choice = new(new("origin/grpc", "refs/remotes/origin/grpc", Snapshots.Id('b'), true, "origin"), main) };
        Assert.True(remote.ShowsMainline); Assert.False(remote.CanRebaseOtherWay);
        Assert.EndsWith("To bring grpc up to date instead, rebase it onto main: switch to it first.", remote.MainlineText);
    }

    [Fact]
    public void WhenTheUpstreamIsNotTheRemotesDefaultBranch_ShouldNotWarnAboutTheBranchOthersBuildOn()
    {
        var (_, _, pushed, summary) = Mainline();
        Assert.False((summary with { Preview = pushed with { UpstreamIsDefault = false } }).ShowsMainline);
    }

    // main and master stand in for the default branch when the remote names none.
    [Fact]
    public void WhenTheRemoteNamesNoDefaultBranch_ShouldTakeMainAsTheBranchOthersBuildOn()
    {
        var (_, _, pushed, summary) = Mainline();
        Assert.True((summary with { Preview = pushed with { UpstreamIsDefault = null } }).ShowsMainline);
    }

    [Fact]
    public void WhenTheRemoteNamesNoDefaultBranchAndTheBranchIsNotMainOrMaster_ShouldOnlyWarnThatItIsOnTheRemote()
    {
        var (main, grpc, pushed, _) = Mainline();
        var develop = new RebaseSummary(new(grpc, main with { Name = "develop", Ref = "refs/heads/develop" }), pushed with { UpstreamIsDefault = null }, Snapshots.Create(branch: "develop"));
        Assert.False(develop.ShowsMainline); Assert.True(develop.ShowsPublished);
    }

    [Fact]
    public void WhenNoCommitOfTheBranchOthersBuildOnIsOnTheRemote_ShouldRewriteItWithoutAWarning()
    {
        var (_, _, pushed, summary) = Mainline();
        Assert.False((summary with { Preview = pushed with { Published = 0 } }).ShowsMainline);
    }

    [Fact]
    public void WhenUncommittedChangesAreInTheWay_ShouldBlockTheRebase()
    {
        var snapshot = Snapshots.Create(branch: "feature", files: [Snapshots.Modified("a.txt"), Snapshots.Untracked("b.txt")]);
        var summary = new RebaseSummary(OntoMain, Replay, snapshot);
        Assert.True(summary.IsBlocked); Assert.False(summary.CanStart);
        Assert.StartsWith("2 files have uncommitted changes. A rebase needs a clean working tree", summary.BlockedText);
        Assert.Equal("2 uncommitted files", summary.BlockedShort);
        Assert.Equal("Checked out · 2 changed files", summary.BranchHint);
    }

    // A submodule's own working tree is its own repository's; Git rebases around it, as the rebase itself is allowed.
    [Fact]
    public void WhenOnlyASubmodulesOwnWorkingTreeChanged_ShouldNotBlockTheRebase()
    {
        var dirty = new FileStatus(Snapshots.Path("lib"), '.', 'M', IsSubmodule: true, SubmoduleHasOwnChanges: true);
        var summary = new RebaseSummary(OntoMain, Replay, Snapshots.Create(branch: "feature", files: [dirty]));
        Assert.Equal((false, true), (summary.IsBlocked, summary.CanStart));
    }

    // Git leaves out 2 merge commits and 1 commit main already has, besides the 7 it replays.
    private static RebaseSummary LeavingOut() =>
        new(OntoMain, Replay with { Merges = 2, AlreadyOnto = 1 }, Snapshots.Create(branch: "feature"));

    // main, checked out and pushed to the remote's default branch with 7 commits, rebased onto the local branch grpc.
    private static (BranchInfo Main, BranchInfo Grpc, RebasePreview Pushed, RebaseSummary Summary) Mainline()
    {
        var main = new BranchInfo("main", "refs/heads/main", Snapshots.HeadId, false, null, "refs/remotes/origin/main");
        var grpc = new BranchInfo("grpc", "refs/heads/grpc", Snapshots.Id('b'), false);
        var pushed = Replay with { Upstream = "origin/main", Published = 7, UpstreamIsDefault = true };
        return (main, grpc, pushed, new RebaseSummary(new(grpc, main), pushed, Snapshots.Create(branch: "main", trackingRemote: "origin")));
    }
}
