using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using Trag.Domain.Diff;
using Trag.Domain.Merge;
using Trag.Domain.Naming;
using Trag.Domain.Rebase;
using Trag.Domain.Status;
using Trag.Domain.Tags;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.History;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Shell;

// Every command of the shell, found by walking its view models' public properties, asked whether it can run before and
// after an outcome starts waiting for review, in each state that offers other writes; a command that takes a row is asked
// with the state's row of that type. The review turns off every write the state offered, whose buttons the banner then
// explains; what stays enabled writes nothing, or reaches the writer, which refuses it and says why on the status line,
// as the branch picker's rows do.
public sealed class ReviewCommandTests
{
    private static readonly string Older = Snapshots.Id('b');

    [Fact]
    public async Task WhenAnOutcomeWaitsForReview_ShouldTurnOffEveryWriteTheStateOffered()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = h.Synced(Snapshots.Staged("staged.txt"), Snapshots.Modified("changed.txt"));
        h.Sync.State = (snapshot, remote, branch) => FakeGitSync.DefaultState(snapshot, remote, branch) with { Ahead = 1, Behind = 1 };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0]; await model.WorkingDiff.Diff.Completion;
        model.Commit.CommitMessage = "Message";
        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null); model.Branches.Draft.Name = "topic";
        model.RemoteSettings.OpenCommand.Execute(null); (model.RemoteSettings.NewName, model.RemoteSettings.NewUrl) = ("upstream", "https://example.invalid/upstream.git");

        Assert.Equal(
        [
            "Shell.Branches.CreateBranchCommand", "Shell.Commit.CommitCommand", "Shell.Commit.StageSelectedCommand", "Shell.Discard.DiscardSelectedCommand",
            "Shell.Discard.OpenStash", "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.OpenCommand", "Shell.RemoteSettings.AddCommand",
            "Shell.Sync.FetchCommand", "Shell.Sync.PushCommand", "Shell.WorkingDiff.DiscardHunkCommand", "Shell.WorkingDiff.TakeHunkCommand"
        ], TurnedOff(model));
    }

    // A commit chosen in the history with the revert, cherry-pick and reset questions and the new tag open on it, beside
    // a stash and a tag of the lists, a file to ignore and a file of a commit.
    [Fact]
    public async Task WhenAnOutcomeWaitsForReview_ShouldTurnOffTheWritesOfTheChosenCommitAndOfEachRow()
    {
        var h = new ShellHarness();
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update", Older), Snapshots.Commit(Older, "Older commit")], false, page);
        h.Branches.Distances[Older] = 1;
        h.Stashes.Add("On main: kept");
        h.Branches.Tags.Add(new TagInfo("v1", Snapshots.HeadId, "Release"));
        h.Reader.Snapshot = h.Synced() with { StashHead = h.Stashes.Head, TagRefs = "one" };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; await model.Stash.List.Completion; await model.Tags.List.Completion;
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        model.Revert.RequestRevertCommand.Execute(null);
        await model.CherryPick.RequestCherryPickCommand.ExecuteAsync(null);
        await model.Reset.RequestResetCommand.ExecuteAsync(null);
        model.Tags.OpenCreateTagCommand.Execute(null); model.Tags.Draft.Name = "v2";

        Assert.Equal(
        [
            "Shell.CherryPick.Question.ConfirmCommand", "Shell.Files.IgnoreCommand", "Shell.Files.RequestRestoreCommand", "Shell.Files.RevertFileCommand",
            "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.OpenCommand", "Shell.Reset.Question.ConfirmCommand", "Shell.Revert.Question.ConfirmCommand",
            "Shell.Stash.ApplyCommand", "Shell.Stash.RequestDropCommand", "Shell.Sync.FetchCommand", "Shell.Sync.PullCommand", "Shell.Sync.PushCommand",
            "Shell.Tags.CreateTagCommand", "Shell.Tags.PushCommand", "Shell.Tags.RequestDeleteCommand"
        ], TurnedOff(model, model.Stash.Rows[0], model.Tags.Visible[0], IgnoreChoices.File(Snapshots.Path("out.log"))!,
            new CommitFileRow(new FileDiffMetadata(Snapshots.Path("a.txt"), "M", "100644", "100644", Snapshots.Id('1'), Snapshots.Id('2')), Older)));
    }

    // A merge, a rebase and a cherry-pick stopped with their files resolved and the abort question open, the merge's file
    // decided by hand, and a merge stopped at a conflict whose block the conflict view shows.
    [Theory]
    [InlineData("merge", new[]
    {
        "Shell.Merge.Abort.ConfirmCommand", "Shell.Merge.FinishMergeCommand", "Shell.Merge.RequestAbortMergeCommand", "Shell.Resolution.RequestReopenCommand"
    })]
    [InlineData("conflict", new[]
    {
        "Shell.ConflictsPage.Conflict.TakeBothCommand", "Shell.ConflictsPage.Conflict.TakeOtherCommand", "Shell.ConflictsPage.Conflict.TakeYoursCommand",
        "Shell.Merge.Abort.ConfirmCommand", "Shell.Merge.RequestAbortMergeCommand", "Shell.Resolution.OpenConflictEditorCommand",
        "Shell.Resolution.RunMergeToolCommand", "Shell.Resolution.UseOtherCommand", "Shell.Resolution.UseYoursCommand"
    })]
    [InlineData("rebase", new[]
    {
        "Shell.Rebase.Abort.ConfirmCommand", "Shell.Rebase.ContinueRebaseCommand", "Shell.Rebase.RequestAbortRebaseCommand", "Shell.Rebase.SkipCommitCommand"
    })]
    [InlineData("cherry-pick", new[] { "Shell.Pick.Abort.ConfirmCommand", "Shell.Pick.FinishPickCommand", "Shell.Pick.RequestAbortPickCommand" })]
    public async Task WhenAnOutcomeWaitsForReviewDuringAnOperation_ShouldTurnOffItsWrites(string state, string[] writes)
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = state switch
        {
            "merge" => Snapshots.Merging() with { Files = [Snapshots.Staged("a")], Resolutions = new([new(Snapshots.Path("a"), Resolution.Edited)], []) },
            "conflict" => Snapshots.Merging() with { Resolutions = new([], [new(Snapshots.Path("a"), 1)]) },
            "rebase" => Snapshots.Rebasing(files: [Snapshots.Staged("a")]),
            _ => Snapshots.Create(operation: RepositoryOperation.CherryPick, files: [Snapshots.Staged("a")]) with { PickHead = Older }
        };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.ConflictsPage.Conflict.Document.Completion;
        foreach (var abort in new IRelayCommand[] { model.Merge.RequestAbortMergeCommand, model.Rebase.RequestAbortRebaseCommand, model.Pick.RequestAbortPickCommand })
            if (abort.CanExecute(null)) abort.Execute(null);

        Assert.Equal(writes, TurnedOff(model, [.. model.ConflictsPage.Conflict.Parts.Where(row => row.Part.IsBlock), .. model.Resolution.Resolved]));
    }

    // Each write only one state offers, with its dialog, question or card open and filled in: a branch renamed, a commit
    // gone to at a branch's tip and off it, the commits a detached HEAD leaves behind, the merge and the rebase of a chosen
    // branch, a publish to an address, a remote edited and removed, a staged file chosen, the stash and discard dialogs,
    // the commits from one on edited, a conflict resolved by hand, submodules not at their commits, a branch rewritten
    // after its push, a remote repository the push did not find, and the question that reopens a decided file, whose own
    // check is not the one every other question asks.
    [Theory]
    [InlineData("rename", new[] { "Shell.Branches.RenameBranchCommand", "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.OpenCommand" })]
    [InlineData("go-to-tip", new[] { "Shell.GoTo.SwitchToTipCommand", "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.OpenCommand" })]
    [InlineData("go-to-commit", new[]
    {
        "Shell.GoTo.CreateBranchAtCommitCommand", "Shell.GoTo.DetachAtCommitCommand", "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.OpenCommand"
    })]
    [InlineData("keep", new[] { "Shell.GoTo.DiscardCommitsCommand", "Shell.GoTo.KeepCommitsCommand" })]
    [InlineData("merge", new[] { "Shell.MergeDialog.Merge.MergeCommand" })]
    [InlineData("rebase", new[] { "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.Rebase.RebaseCommand", "Shell.MergeDialog.Rebase.RebaseOtherWayCommand" })]
    [InlineData("publish", new[] { "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.OpenCommand", "Shell.Publish.PublishCommand" })]
    [InlineData("remote-edit", new[]
    {
        "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.OpenCommand", "Shell.RemoteSettings.SaveCommand", "Shell.Sync.FetchCommand", "Shell.Sync.PullCommand",
        "Shell.Sync.PushCommand"
    })]
    [InlineData("remote-remove", new[]
    {
        "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.OpenCommand", "Shell.RemoteSettings.ConfirmRemoveCommand", "Shell.Sync.FetchCommand",
        "Shell.Sync.PullCommand", "Shell.Sync.PushCommand"
    })]
    [InlineData("unstage", new[]
    {
        "Shell.Commit.UnstageSelectedCommand", "Shell.Discard.OpenStash", "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.OpenCommand",
        "Shell.WorkingDiff.TakeHunkCommand"
    })]
    [InlineData("stash", new[] { "Shell.Discard.OpenStash", "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.OpenCommand", "Shell.Stash.CreateStashCommand" })]
    [InlineData("discard", new[]
    {
        "Shell.Commit.StageSelectedCommand", "Shell.Discard.Confirmation.ConfirmCommand", "Shell.Discard.DiscardSelectedCommand", "Shell.Discard.OpenStash",
        "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.OpenCommand", "Shell.WorkingDiff.DiscardHunkCommand", "Shell.WorkingDiff.TakeHunkCommand"
    })]
    [InlineData("rewrite", new[] { "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.OpenCommand", "Shell.Rewrite.Question.ConfirmCommand" })]
    [InlineData("resolution", new[]
    {
        "Shell.Merge.RequestAbortMergeCommand", "Shell.Resolution.OpenConflictEditorCommand", "Shell.Resolution.RunMergeToolCommand",
        "Shell.Resolution.StageResolutionCommand", "Shell.Resolution.UseOtherCommand", "Shell.Resolution.UseYoursCommand"
    })]
    [InlineData("reopen", new[] { "Shell.Merge.FinishMergeCommand", "Shell.Merge.RequestAbortMergeCommand", "Shell.Resolution.Reopen.ConfirmCommand" })]
    [InlineData("submodules", new[] { "Shell.FileStatus.UpdateSubmodulesCommand", "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.OpenCommand" })]
    [InlineData("force-push", new[]
    {
        "Shell.FileStatusPage.ForcePush.Replace.ConfirmCommand", "Shell.FileStatusPage.ForcePush.RequestForcePushCommand", "Shell.MergeDialog.Merge.OpenCommand",
        "Shell.MergeDialog.OpenCommand", "Shell.Sync.FetchCommand", "Shell.Sync.PushCommand"
    })]
    [InlineData("missing-repository", new[]
    {
        "Shell.MergeDialog.Merge.OpenCommand", "Shell.MergeDialog.OpenCommand", "Shell.Sync.CreateMissingRepositoryCommand", "Shell.Sync.FetchCommand",
        "Shell.Sync.PullCommand", "Shell.Sync.PushCommand"
    })]
    public async Task WhenAnOutcomeWaitsForReviewWhereOnlyOneStateOffersAWrite_ShouldTurnItOff(string state, string[] writes)
    {
        var h = new ShellHarness();
        h.Branches.Local.Add("topic");
        h.Branches.Heads["topic"] = state == "go-to-tip" ? Older : Snapshots.HeadId;
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update", Older), Snapshots.Commit(Older, "Older commit")], false, page);
        h.Branches.Preview = (_, _) => new(Snapshots.HeadId, Older, [], 2, 1, []);
        // The branch others build on, so the rebase also offers the other way round.
        h.Branches.RebasePreview = _ => new(Snapshots.HeadId, Older, [Snapshots.Commit(Snapshots.Id('c'), "Add login")], 40, 1, [], "origin/main", 40) { UpstreamIsDefault = true };
        if (state == "keep") (h.Reader.Snapshot, h.Branches.Current) = (Snapshots.Create(branch: null, head: Older, detachedCommits: 1), null);
        if (state.StartsWith("remote", StringComparison.Ordinal) || state == "force-push") h.Reader.Snapshot = h.Synced();
        if (state is "unstage" or "stash" or "discard") h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Staged("staged.txt"), Snapshots.Modified("changed.txt")]);
        h.Branches.Rewrites[Older] = new(Snapshots.Id('a'), [new(Older, "Older commit", "Older commit", RewriteAction.Pick, "Older commit"),
            new(Snapshots.HeadId, "Update", "Update", RewriteAction.Pick, "Update")], 0);
        if (state == "resolution") h.Reader.Snapshot = Snapshots.Merging() with { Resolutions = new([], [new(Snapshots.Path("a"), 0)]) };
        if (state == "reopen") h.Reader.Snapshot = Snapshots.Merging() with { Files = [Snapshots.Staged("a")], Resolutions = new([new(Snapshots.Path("a"), Resolution.Edited)], []) };
        if (state == "submodules") h.Reader.Snapshot = Snapshots.Create() with { SubmodulesToUpdate = ["sub"] };
        // The branch was rewritten after its push, so only replacing the remote branch sends it.
        if (state == "force-push") h.Sync.State = (snapshot, remote, branch) => FakeGitSync.DefaultState(snapshot, remote, branch) with
            { Ahead = 2, Behind = 3, Rewritten = true, TrackingHead = Snapshots.Id('e') };
        if (state == "missing-repository")
        {
            h.Catalog.Accounts.Add(FakeAccountService.GitHub());
            h.Reader.Snapshot = Snapshots.Create(trackingRemote: "origin", upstream: "origin/main", remotes: [Snapshots.Remote(url: "https://github.com/alice/fixture")]);
            h.Sync.State = (snapshot, remote, branch) => FakeGitSync.DefaultState(snapshot, remote, branch) with { Ahead = 1 };
            h.Writer.Result = GitWriteResult.Rejected("The host has no repository at this address.") with { MissingRepository = true };
        }
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; await model.Branches.List.Completion;
        switch (state)
        {
            case "rename":
                model.Branches.RequestRenameCommand.Execute(model.Branches.Rows.Local.Single(row => row.Name == "topic")); model.Branches.Rename.Name = "renamed";
                break;
            case "go-to-tip" or "go-to-commit":
                model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
                model.History.SelectedCommit = model.History.Page.Commits[1]; await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null);
                break;
            case "keep":
                await model.Branches.SwitchToBranchCommand.ExecuteAsync(model.Branches.Rows.Local.Single(row => row.Name == "main"));
                break;
            case "merge" or "rebase":
                await model.MergeDialog.OpenCommand.ExecuteAsync(null);
                model.MergeDialog.Merge.Draft.From = model.MergeDialog.Merge.Draft.FromOptions.Single(row => row.Name == "topic"); await model.MergeDialog.Merge.Preview.Completion;
                if (state == "rebase") { await model.MergeDialog.ShowRebaseCommand.ExecuteAsync(null); await model.MergeDialog.Rebase.Preview.Completion; }
                break;
            case "publish":
                await model.Publish.OpenPublishCommand.ExecuteAsync(null); model.Publish.UsePublishUrlCommand.Execute(null);
                model.Publish.Draft.RemoteUrl = "https://example.invalid/fixture.git";
                break;
            case "remote-edit":
                model.RemoteSettings.OpenCommand.Execute(null); await model.RemoteSettings.BeginEditCommand.ExecuteAsync(model.RemoteSettings.Remotes[0]);
                model.RemoteSettings.EditUrl = "https://example.invalid/other.git";
                break;
            case "remote-remove":
                model.RemoteSettings.OpenCommand.Execute(null); model.RemoteSettings.BeginRemoveCommand.Execute(model.RemoteSettings.Remotes[0]);
                break;
            case "unstage":
                model.FileStatus.SelectedStaged = (StatusRow)model.FileStatus.StagedFiles[0]; await model.WorkingDiff.Diff.Completion;
                break;
            case "stash":
                model.Stash.OpenStashCommand.Execute(null);
                break;
            case "discard":
                model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0]; await model.WorkingDiff.Diff.Completion;
                model.Discard.DiscardSelectedCommand.Execute(null);
                break;
            case "rewrite":
                model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
                model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
                await model.Rewrite.RequestRewriteCommand.ExecuteAsync(null); model.Rewrite.MoveTo(model.Rewrite.Rows[1], 0);
                break;
            case "reopen":
                await model.Resolution.RequestReopenCommand.ExecuteAsync(model.Resolution.Resolved[0]);
                break;
            case "force-push":
                model.FileStatusPage.ForcePush.RequestForcePushCommand.Execute(null);
                break;
            case "missing-repository":
                await model.Sync.PushCommand.ExecuteAsync(null); await model.Sync.MissingRepositoryCompletion;
                break;
        }

        Assert.Equal(writes, TurnedOff(model));
    }

    // What the review turned off; it turns nothing on.
    private static IEnumerable<string> TurnedOff(ShellViewModel model, params object[] rows)
    {
        var before = Enabled(model, rows);
        model.Repository.NeedsReview = true;
        var after = Enabled(model, rows);
        Assert.Empty(after.Except(before));
        return before.Except(after).Order(StringComparer.Ordinal);
    }

    // The commands that can run now, each named by the path of properties it was first reached by.
    private static List<string> Enabled(ShellViewModel model, object[] rows)
    {
        var enabled = new List<string>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(string Path, object Node)>([("Shell", model)]);
        while (queue.TryDequeue(out var item))
        {
            if (!seen.Add(item.Node)) continue;
            foreach (var property in item.Node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.GetIndexParameters().Length == 0))
            {
                var value = property.GetValue(item.Node);
                var path = item.Path + "." + property.Name;
                if (value is IRelayCommand command) { if (seen.Add(command) && command.CanExecute(Row(command, rows))) enabled.Add(path); }
                else if (value?.GetType().Namespace?.StartsWith("Trag.ViewModels", StringComparison.Ordinal) == true) queue.Enqueue((path, value));
            }
        }
        return enabled;
    }

    // The row a command of a row type is asked with; null for any other command.
    private static object? Row(IRelayCommand command, object[] rows) =>
        command.GetType().GetInterfaces().FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRelayCommand<>))?.GetGenericArguments()[0] is { } parameter
            ? rows.FirstOrDefault(parameter.IsInstanceOfType) : null;
}
