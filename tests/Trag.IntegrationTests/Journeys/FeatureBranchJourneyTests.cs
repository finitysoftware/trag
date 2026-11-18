using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Rebase;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.IntegrationTests.Journeys;

// A feature branch from its first commit to its merge into main, over real Git and a bare remote on disk.
public sealed class FeatureBranchJourneyTests
{
    [Fact]
    public async Task WhenAFeatureBranchIsCommittedRebasedRewrittenForcePushedAndMerged_ShouldLeaveMainWithItsThreeChangesInTwoCommitsAndAMerge()
    {
        await using var journey = await JourneyShell.StartAsync();
        var shell = journey.Shell;
        var (work, hub, start, mainTip) = await FeatureBranch.OpenMainAsync(journey);

        await FeatureBranch.CreateFromTheFirstCommitAsync(journey, start);

        // The first commit, staged hunk by hunk: both edges of app.txt.
        await File.WriteAllTextAsync(Path.Combine(work, "app.txt"), FeatureBranch.App("line 2 on feature", "line 11 on feature"), journey.Fixture.Token);
        await ShellHarness.Until(() => shell.FileStatus.UnstagedFiles.Count == 1 && !shell.Repository.IsBusy, "the watcher to show the edited app.txt");
        shell.FileStatus.SelectedUnstaged = (StatusRow)shell.FileStatus.UnstagedFiles[0]!;
        await ShellHarness.Until(() => Hunks(shell.WorkingDiff.Diff.Document.Lines).Length == 2, "both hunks of app.txt in the diff");
        var shown = shell.WorkingDiff.Diff.Document.Lines;
        var hunks = Hunks(shown);
        await shell.WorkingDiff.TakeHunkCommand.ExecuteAsync(new HunkChoice(shown, hunks[0].Lines));
        await shell.Sync.SyncCompletion;
        Assert.Equal(FeatureBranch.App("line 2 on feature").TrimEnd(), await journey.Git(work, "show", ":app.txt"));
        await ShellHarness.Until(() => !shell.Repository.IsBusy && shell.WorkingDiff.Diff.IsCurrent && Hunks(shell.WorkingDiff.Diff.Document.Lines).Length == 1,
            "the rest of app.txt in the diff");
        shown = shell.WorkingDiff.Diff.Document.Lines;
        await shell.WorkingDiff.TakeHunkCommand.ExecuteAsync(new HunkChoice(shown, Hunks(shown)[0].Lines));
        await shell.Sync.SyncCompletion;
        Assert.Equal(FeatureBranch.App("line 2 on feature", "line 11 on feature").TrimEnd(), await journey.Git(work, "show", ":app.txt"));
        await FeatureBranch.CommitAsync(journey, "Change the edges");

        await FeatureBranch.CommitNotesAsync(journey);
        await FeatureBranch.PublishAsync(journey);
        var pushed = await journey.Git(work, "rev-parse", "HEAD");

        await FeatureBranch.RebaseOntoMainAsync(journey, mainTip);
        Assert.True(shell.FileStatusPage.ForcePush.Summary.Shows);
        var rebasedTree = await journey.Git(work, "rev-parse", "feature^{tree}");

        // History → the feature's first commit → Edit commits from here: the last two squashed, the first reworded.
        shell.Navigator.ShowHistory();
        await shell.History.HistoryCompletion;
        var first = (await journey.Git(work, "rev-list", "--reverse", "main..feature")).Split('\n')[0];
        shell.History.SelectedCommit = shell.History.Page.Commits.Single(commit => commit.Id == first);
        await shell.Rewrite.RequestRewriteCommand.ExecuteAsync(null);
        var rows = shell.Rewrite.Rows;
        Assert.Equal(["Change the edges", "Add notes", "Extend notes"], rows.Select(row => row.Subject));
        rows[2].Action = RewriteAction.Squash;
        shell.Rewrite.SelectedRow = rows[1];
        Assert.True(shell.Rewrite.CanEditMessage);
        shell.Rewrite.Message = "Add notes";
        rows[0].Action = RewriteAction.Reword;
        shell.Rewrite.SelectedRow = rows[0];
        shell.Rewrite.Message = "Change both edges";
        Assert.Equal("", shell.Rewrite.Refusal);
        await shell.Rewrite.Question.ConfirmCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(shell.Rewrite.Question.IsOpen, shell.Rewrite.Question.Message);
        Assert.Equal("Add notes\nChange both edges", await journey.Git(work, "log", "--format=%s", "main..feature"));
        Assert.Equal("Add notes", await journey.Git(work, "log", "-n1", "--format=%B", "feature"));
        Assert.Equal(rebasedTree, await journey.Git(work, "rev-parse", "feature^{tree}"));
        Assert.Equal(mainTip, await journey.Git(work, "rev-parse", "feature~2"));

        // The banner over the file status: force push with lease replaces the remote branch.
        shell.Navigator.ShowFileStatus();
        var rewritten = await journey.Git(work, "rev-parse", "feature");
        Assert.Equal(pushed, await journey.Git(hub, "rev-parse", "refs/heads/feature"));
        await FeatureBranch.ForcePushAsync(journey);

        // The merge dialog: feature into main, always with a merge commit, and main pushed afterwards.
        await shell.MergeDialog.OpenCommand.ExecuteAsync(null);
        var merge = shell.MergeDialog.Merge;
        merge.Draft.Into = merge.Draft.IntoOptions.Single(row => row.Name == "main");
        merge.Draft.From = merge.Draft.FromOptions.Single(row => row.Name == "feature");
        merge.Draft.NoFastForward = true;
        merge.Draft.PushAfter = true;
        await ShellHarness.Until(() => merge.MergeCommand.CanExecute(null), "the merge preview");
        await merge.MergeCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(merge.Draft.IsOpen, merge.Draft.Notice);
        var merged = await journey.Git(work, "rev-parse", "main");
        Assert.Equal($"{merged} {mainTip} {rewritten}", await journey.Git(work, "rev-list", "--parents", "-n1", "main"));
        Assert.Equal(merged, await journey.Git(hub, "rev-parse", "refs/heads/main"));
        Assert.Equal(merged, await journey.Git(work, "rev-parse", "refs/remotes/origin/main"));
        Assert.Equal("refs/heads/feature", await journey.Git(work, "symbolic-ref", "HEAD"));

        // Back on main, the local branch goes without a question, since main has it, and the remote one after it.
        await shell.Branches.SwitchToBranchCommand.ExecuteAsync(shell.Branches.Rows.Local.Single(row => row.Name == "main"));
        await shell.Sync.SyncCompletion;
        await shell.Branches.List.Completion;
        Assert.Equal("refs/heads/main", await journey.Git(work, "symbolic-ref", "HEAD"));
        await shell.Branches.RequestDeleteCommand.ExecuteAsync(shell.Branches.Rows.Local.Single(row => row.Name == "feature"));
        await shell.Sync.SyncCompletion;
        await shell.Branches.List.Completion;
        Assert.False(shell.Branches.Delete.IsOpen);
        Assert.Equal("", await journey.Git(work, "for-each-ref", "refs/heads/feature"));
        await shell.Branches.RequestDeleteCommand.ExecuteAsync(shell.Branches.Rows.Remote.Single(row => row.Name == "origin/feature"));
        Assert.True(shell.Branches.Delete.IsOpen);
        await shell.Branches.Delete.ConfirmCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        await shell.Branches.List.Completion;
        Assert.False(shell.Branches.Delete.IsOpen, shell.Branches.Delete.Message);
        Assert.Equal("", await journey.Git(hub, "for-each-ref", "refs/heads/feature"));
        Assert.Equal("", await journey.Git(work, "for-each-ref", "refs/remotes/origin/feature"));
        Assert.DoesNotContain(shell.Branches.Rows.Remote, row => row.Name == "origin/feature");

        // main holds exactly the three changes, in two commits and the merge.
        Assert.Equal(merged, await journey.Git(work, "rev-parse", "HEAD"));
        Assert.Equal("3", await journey.Git(work, "rev-list", "--count", mainTip + "..main"));
        Assert.Equal("M\tapp.txt\nA\tnotes.txt", await journey.Git(work, "diff", "--name-status", mainTip, "main"));
        Assert.Equal(FeatureBranch.App("line 2 on feature", "line 11 on feature").TrimEnd(), await journey.Git(work, "show", "main:app.txt"));
        Assert.Equal("First note.\nSecond note.", await journey.Git(work, "show", "main:notes.txt"));
        Assert.Equal("", await journey.Git(work, "status", "--porcelain"));
        await journey.Git(work, "fsck", "--no-progress");
        await journey.Git(hub, "fsck", "--no-progress");
    }

    // The runs of changed lines of a diff, each a hunk the user can take on its own.
    private static DiffBlock[] Hunks(IReadOnlyList<PatchLine> lines) =>
        [.. Enumerable.Range(0, lines.Count).Where(index => lines[index].IsChange && (index == 0 || !lines[index - 1].IsChange))
            .Select(index => DiffBlocks.At(lines, index)!.Value)];
}
