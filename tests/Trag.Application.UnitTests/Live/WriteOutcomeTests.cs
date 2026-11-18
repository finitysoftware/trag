using Trag.Application.Live;
using Trag.Domain.Merge;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Live;

// What a finished write means to the panes beside it: a sync reports in its own panel, a commit's message goes once it is
// committed, an amend ends, and a write of files clears the selection it acted on, while a hunk keeps its file.
public sealed class WriteOutcomeTests
{
    private static readonly GitWriteResult Done = new(GitWriteOutcome.Succeeded, "Done.");
    private static readonly GitWriteResult Refused = GitWriteResult.Rejected("No.");
    private static readonly Trag.Domain.Status.RepositorySnapshot Expected = Snapshots.Create();

    [Fact]
    public void WhenASyncEnds_ShouldBeTheSyncPanels()
    {
        Assert.True(new WriteOutcome(new SyncRequest(Expected, FakeGitSync.DefaultState(Expected, "origin", null), SyncAction.Fetch), Done).IsSync);
        Assert.False(new WriteOutcome(new StageRequest(Expected, [Snapshots.Path("a.txt")]), Done).IsSync);
    }

    [Fact]
    public void WhenACommitSucceeds_ShouldNameTheMessageItCommittedAndAnAmendShouldEnd()
    {
        Assert.Equal("Fix it", new WriteOutcome(new CommitRequest(Expected, "Fix it"), Done with { CommitId = Snapshots.Id('c') }).CommittedMessage);
        Assert.Null(new WriteOutcome(new CommitRequest(Expected, "Fix it"), Done).CommittedMessage);
        Assert.Null(new WriteOutcome(new CommitRequest(Expected, "Fix it"), Refused with { CommitId = Snapshots.Id('c') }).CommittedMessage);
        Assert.True(new WriteOutcome(new CommitRequest(Expected, "Fix it", Amend: true), Done).EndsAmend);
        Assert.Null(new WriteOutcome(new CommitRequest(Expected, "Fix it", Amend: true), Done with { CommitId = Snapshots.Id('c') }).CommittedMessage);
        Assert.False(new WriteOutcome(new CommitRequest(Expected, "Fix it", Amend: true), Refused).EndsAmend);
    }

    [Fact]
    public void WhenAWriteOfFilesSucceeds_ShouldClearTheSelectionButAHunkOrACommitShouldNot()
    {
        Assert.True(new WriteOutcome(new StageRequest(Expected, [Snapshots.Path("a.txt")]), Done).ClearsSelection);
        Assert.False(new WriteOutcome(new StageRequest(Expected, [Snapshots.Path("a.txt")]), Refused).ClearsSelection);
        Assert.False(new WriteOutcome(new PatchRequest(Expected, Snapshots.Path("a.txt"), "p", PatchAction.Stage, Lines: false), Done).ClearsSelection);
        Assert.False(new WriteOutcome(new CommitRequest(Expected, "Fix it"), Done).ClearsSelection);
    }

    // Only a write of the selected files is done with them: the lists, the discard question and the conflicts view act on
    // the selection. Any other write leaves it, and a file it took away leaves the lists by itself.
    [Fact]
    public void WhenAWriteSucceeds_ShouldClearTheSelectionOnlyWhenItActedOnTheSelectedFiles()
    {
        var path = Snapshots.Path("a.txt");
        GitWriteRequest[] selected = [new UnstageRequest(Expected, [path]), new DiscardRequest(Expected, [path]),
            new ConflictSideRequest(Expected, path, ConflictChoice.Mine), new MergeToolRequest(Expected, path, false), new ReopenConflictRequest(Expected, path)];
        GitWriteRequest[] other = [new SyncRequest(Expected, FakeGitSync.DefaultState(Expected, "origin", null), SyncAction.Fetch),
            new TagDeleteRequest(Expected, "v1"), new RemoteAddRequest(Expected, "origin", "https://example.invalid/a.git"),
            new IgnoreRequest(Expected, path, "a.txt"), new RestoreFileRequest(Expected, Snapshots.Id('c'), path)];

        Assert.All(selected, request => Assert.True(new WriteOutcome(request, Done).ClearsSelection, request.GetType().Name));
        Assert.All(other, request => Assert.False(new WriteOutcome(request, Done).ClearsSelection, request.GetType().Name));
    }
}
