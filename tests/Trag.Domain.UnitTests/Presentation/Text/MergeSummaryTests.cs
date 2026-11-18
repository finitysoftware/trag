using Trag.Domain.Branches;
using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Presentation.Text;

// What the merge dialog says for each kind of merge: a fast-forward, a merge commit, one with conflicts, one blocked by
// uncommitted changes, nothing to merge, and a merge into a branch that is not checked out.
public sealed class MergeSummaryTests
{
    private static readonly BranchInfo Main = new("main", "refs/heads/main", Snapshots.HeadId, false, null, "refs/remotes/origin/main");
    private static readonly BranchInfo Feature = new("feature", "refs/heads/feature", Snapshots.Id('b'), false);
    private static readonly MergePreview Ahead = new(Snapshots.HeadId, Snapshots.Id('b'), [Snapshots.Commit(Snapshots.Id('b'), "Add login")], 3, 0, []);
    private static readonly MergeChoice IntoMain = new(Feature, Main, "main") { Message = "Merge branch 'feature' into main" };

    [Fact]
    public void WhenTheTargetHasNoCommitsOfItsOwn_ShouldSayItFastForwardsAndCannotConflict()
    {
        var summary = FastForward();
        Assert.Equal(("3 commits main doesn't have", "Checked out"), (summary.FromHint, summary.IntoHint));
        Assert.True(summary.IsFastForward); Assert.False(summary.CreatesMergeCommit); Assert.True(summary.ShowsNoFastForward);
        Assert.Equal("Fast-forward: main moves ahead", summary.ResultTitle);
        Assert.Equal(("No conflicts possible", true), (summary.CheckText, summary.ShowsCheck));
        Assert.Equal("Merge feature into main", summary.Action);
        Assert.Equal(("COMMITS COMING IN · 3", "and 2 more"), (summary.CommitsCaption, summary.MoreCommits));
        Assert.True(summary.CanStart);
    }

    [Fact]
    public void WhenTheTargetHasAnUpstream_ShouldOfferToPushItAfterMerging()
    {
        var summary = FastForward();
        Assert.True(summary.CanPush);
        Assert.Equal("Push main after merging", summary.PushLabel);
    }

    [Fact]
    public void WhenBothBranchesHaveCommitsOfTheirOwn_ShouldCreateAMergeCommitAndReportWhatMergeTreeFound()
    {
        var (_, summary) = DivergedMerge();
        Assert.Equal("Checked out · 2 commits of its own", summary.IntoHint);
        Assert.True(summary.CreatesMergeCommit); Assert.False(summary.ShowsNoFastForward);
        Assert.Equal("A merge commit will be created", summary.ResultTitle);
        Assert.Equal("No conflicts expected", summary.CheckText);
        Assert.True(summary.CanStart);
    }

    [Fact]
    public void WhenAMergeCommitHasABlankMessage_ShouldNotStart()
    {
        var (_, summary) = DivergedMerge();
        Assert.False((summary with { Choice = IntoMain with { Message = " " } }).CanStart);
    }

    [Fact]
    public void WhenGitCannotCheckForConflicts_ShouldSaySo()
    {
        var (diverged, summary) = DivergedMerge();
        Assert.Equal("Conflicts could not be checked", (summary with { Preview = diverged with { Conflicts = null } }).CheckText);
    }

    // Branches that started apart, or whose common commit a shallow clone lacks: Git refuses to merge them, and merge-tree
    // cannot check them either.
    [Fact]
    public void WhenTheHistoriesAreUnrelated_ShouldSaySoAndDisableMerge()
    {
        var (diverged, summary) = DivergedMerge();
        var unrelated = summary with { Preview = diverged with { Conflicts = null, SharesNoCommit = true } };
        Assert.Equal(("Git will not merge these branches", "These branches share no commit in this repository, so Git will not merge them."),
            (unrelated.ResultTitle, unrelated.ResultText));
        Assert.Equal((false, false, false), (unrelated.CreatesMergeCommit, unrelated.ShowsCheck, unrelated.CanStart));
    }

    [Fact]
    public void WhenAFastForwardIsTurnedOff_ShouldCreateAMergeCommitAndSayGitCouldHaveFastForwarded()
    {
        var forced = new MergeSummary(IntoMain with { NoFastForward = true }, Ahead, Snapshots.Create(branch: "main"));
        Assert.True(forced.CreatesMergeCommit); Assert.False(forced.IsFastForward);
        Assert.StartsWith("Git could fast-forward", forced.ResultText);
    }

    [Fact]
    public void WhenFilesWillConflict_ShouldListThemAndChangeTheButtonToResolveConflicts()
    {
        var paths = Enumerable.Range(1, 8).Select(i => Snapshots.Path("src/file " + i + ".cs")).ToArray();
        var summary = new MergeSummary(IntoMain, Ahead with { OwnCount = 1, Conflicts = paths }, Snapshots.Create(branch: "main"));
        Assert.True(summary.HasConflicts); Assert.False(summary.ShowsCheck);
        Assert.Equal("8 files will conflict", summary.ConflictTitle);
        Assert.Equal(MergeSummary.ListedConflicts, summary.ConflictFiles.Count);
        Assert.Equal("and 2 more files", summary.ConflictMore);
        Assert.Equal("Merge and resolve conflicts…", summary.Action);
        Assert.StartsWith("Trag starts the merge", summary.ConflictText);
        Assert.True(summary.CanStart);
    }

    [Fact]
    public void WhenUncommittedChangesAreInTheWay_ShouldBlockTheMergeAndStillShowTheConflicts()
    {
        var snapshot = Snapshots.Create(branch: "main", files: [Snapshots.Modified("a.txt"), Snapshots.Untracked("b.txt"), Snapshots.Staged("c.txt")]);
        var summary = new MergeSummary(IntoMain, Ahead with { OwnCount = 1, Conflicts = [Snapshots.Path("a.txt")] }, snapshot);
        Assert.True(summary.IsBlocked); Assert.False(summary.CanStart);
        Assert.StartsWith("3 files have uncommitted changes.", summary.BlockedText);
        Assert.Equal("3 uncommitted files", summary.BlockedShort);
        Assert.Equal("Checked out · 3 changed files", summary.IntoHint);
        Assert.True(summary.HasConflicts);
        Assert.StartsWith("Once your changes are saved", summary.ConflictText);
    }

    // A submodule's own working tree is its own repository's; Git merges around it, as the merge itself is allowed.
    [Fact]
    public void WhenOnlyASubmodulesOwnWorkingTreeChanged_ShouldNotBlockTheMergeNorCountTheSubmodule()
    {
        var dirty = new FileStatus(Snapshots.Path("lib"), '.', 'M', IsSubmodule: true, SubmoduleHasOwnChanges: true);
        var alone = new MergeSummary(IntoMain, Ahead, Snapshots.Create(branch: "main", files: [dirty]));
        var withFile = new MergeSummary(IntoMain, Ahead, Snapshots.Create(branch: "main", files: [dirty, Snapshots.Modified("a.txt")]));
        Assert.Equal((false, true, "1 uncommitted file"), (alone.IsBlocked, alone.CanStart, withFile.BlockedShort));
    }

    [Fact]
    public void WhenTheTargetAlreadyHasEveryCommit_ShouldSayItIsUpToDateAndNotStart()
    {
        var upToDate = new MergeSummary(IntoMain, Ahead with { IncomingCount = 0, Incoming = [] }, Snapshots.Create(branch: "main"));
        Assert.Equal("Already up to date", upToDate.ResultTitle); Assert.False(upToDate.ShowsResult);
        Assert.Equal("Nothing main doesn't have", upToDate.FromHint);
        Assert.False(upToDate.CanStart); Assert.Equal("Merge", upToDate.Action);
    }

    // While Git reads, the button does not name a direction it has not confirmed yet.
    [Fact]
    public void WhenThePreviewIsNotReadYet_ShouldShowNoResultAndNotStart()
    {
        var unread = new MergeSummary(IntoMain, MergePreview.None, Snapshots.Create(branch: "main"));
        Assert.Equal(("", "", ""), (unread.ResultTitle, unread.FromHint, unread.ResultText));
        Assert.Empty(unread.Commits); Assert.False(unread.CanStart);
        Assert.Equal("Merge", unread.Action);
    }

    [Fact]
    public void WhenNothingIsChosenYet_ShouldCaptionTheButtonMerge() => Assert.Equal("Merge", MergeSummary.Empty.Action);

    [Fact]
    public void WhenTheTargetIsNotCheckedOut_ShouldSayTragSwitchesToItAndBack()
    {
        var (_, summary) = IntoFeature();
        Assert.Equal("Not checked out", summary.IntoHint);
        Assert.Equal("feature is not checked out. Trag switches to feature, merges, then switches back to main.", summary.SwitchText);
        Assert.Equal("Stay on feature after merging", summary.StayLabel);
    }

    [Fact]
    public void WhenTheUserStaysOnATargetThatIsNotCheckedOut_ShouldSayTragStaysThereAfterMerging()
    {
        var (choice, summary) = IntoFeature();
        Assert.Equal("feature is not checked out. Trag switches to feature and merges; you stay there afterwards.", (summary with { Choice = choice with { StayOnTarget = true } }).SwitchText);
    }

    [Fact]
    public void WhenTheTargetHasNoUpstream_ShouldNotOfferToPushIt()
    {
        var (_, summary) = IntoFeature();
        Assert.False(summary.CanPush);
    }

    // feature into main, checked out, with main's upstream known.
    private static MergeSummary FastForward() => new(IntoMain, Ahead, Snapshots.Create(branch: "main"));

    // main has 2 commits of its own, so feature cannot fast-forward into it.
    private static (MergePreview Diverged, MergeSummary Summary) DivergedMerge()
    {
        var diverged = Ahead with { OwnCount = 2 };
        return (diverged, new MergeSummary(IntoMain, diverged, Snapshots.Create(branch: "main")));
    }

    // main into feature while main is checked out with a change; feature has no upstream.
    private static (MergeChoice Choice, MergeSummary Summary) IntoFeature()
    {
        var choice = new MergeChoice(Main, Feature, "main");
        return (choice, new MergeSummary(choice, Ahead, Snapshots.Create(branch: "main", files: [Snapshots.Modified("a.txt")])));
    }
}
