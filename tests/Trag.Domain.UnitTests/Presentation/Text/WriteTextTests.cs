using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Domain.Rebase;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.Domain.Status;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class WriteTextTests
{
    // A sync and a publish, and the writes that finish or leave a merge or a rebase, report in the sync panel too; the
    // write that starts a rebase and ordinary writes do not.
    [Fact]
    public void WhenAWriteSyncsOrEndsAnOperation_ShouldReportInTheSyncPanel()
    {
        var snapshot = Snapshots.Create();
        Assert.True(WriteText.ReportsInSyncPanel(new SyncRequest(snapshot, FakeGitSync.DefaultState(snapshot, "origin", null), SyncAction.Fetch)));
        Assert.True(WriteText.ReportsInSyncPanel(new PublishRequest(snapshot, PublishOperation.PushTo("origin", "main", null))));
        Assert.True(WriteText.ReportsInSyncPanel(new CommitRequest(snapshot, "m", FinishesMerge: true)));
        Assert.True(WriteText.ReportsInSyncPanel(new RebaseSkipRequest(snapshot)));
        Assert.True(WriteText.ReportsInSyncPanel(new ConflictSideRequest(snapshot, Snapshots.Path("a"), ConflictChoice.Mine)));
        Assert.False(WriteText.ReportsInSyncPanel(new RebaseRequest(snapshot, "main", Snapshots.Id('b'))));
        Assert.False(WriteText.ReportsInSyncPanel(new StageRequest(snapshot, [Snapshots.Path("a")])));
    }

    // Each kind of write says what it does while it runs; a merge and a rebase name their branch, a branch write says which
    // move it is, and a commit's sentence is the one every other write falls back to.
    [Fact]
    public void WhenAWriteRuns_ShouldSayWhatItDoes()
    {
        var expected = Snapshots.Create();
        GitPath[] paths = [Snapshots.Path("a.txt")];
        GitWriteRequest[] requests =
        [
            new StageRequest(expected, paths), new DiscardRequest(expected, paths), new MergeRequest(expected, "topic", "h", false, "m"),
            new RebaseRequest(expected, "main", "h"), new BranchRequest(expected, new("x"), BranchAction.Switch),
            new BranchRequest(expected, new("x"), BranchAction.Create), new BranchRequest(expected, new("x"), BranchAction.Checkout),
            new BranchRequest(expected, new("x"), BranchAction.Delete), new RemoteBranchDeleteRequest(expected, "origin", "x"),
            new TagRequest(expected, new("v1.0", Snapshots.HeadId)), new TagDeleteRequest(expected, "v1.0"), new RevertRequest(expected, Snapshots.HeadId), new CherryPickRequest(expected, Snapshots.HeadId), new PickContinueRequest(expected), new PickAbortRequest(expected),
            new RemoteTagRequest(expected, "origin", "v1.0", RemoteTagAction.Push),
            new RemoteTagRequest(expected, "origin", "v1.0", RemoteTagAction.Delete),
            new CommitRequest(expected, "m", FinishesMerge: true), new CommitRequest(expected, "m"), new CommitRequest(expected, "m", Amend: true),
            new ResetRequest(expected, Snapshots.Id('b'), ResetMode.Soft),
            new RemoteAddRequest(expected, "fork", "u"), new RemoteEditRequest(expected, "fork", "fork", "u"), new RemoteRemoveRequest(expected, "fork"),
            new PatchRequest(expected, paths[0], "p", PatchAction.Stage, Lines: false), new PatchRequest(expected, paths[0], "p", PatchAction.Unstage, Lines: false),
            new PatchRequest(expected, paths[0], "p", PatchAction.Discard, Lines: false), new PatchRequest(expected, paths[0], "p", PatchAction.Stage, Lines: true),
            new PatchRequest(expected, paths[0], "p", PatchAction.Unstage, Lines: true), new PatchRequest(expected, paths[0], "p", PatchAction.Discard, Lines: true),
            new RewriteRequest(expected, Snapshots.Id('b'), [new(Snapshots.Id('c'), "s", "m", RewriteAction.Pick, "m")]),
            new IgnoreRequest(expected, paths[0], "/a.txt"), new RestoreFileRequest(expected, Snapshots.Id('c'), paths[0]),
            new RevertFileRequest(expected, Snapshots.Id('c'), paths[0], null), new SubmoduleUpdateRequest(expected),
            new LfsDownloadRequest(expected, "origin", Snapshots.Id('c')),
        ];
        Assert.Equal(["Staging selected whole files…", "Discarding changes…", "Merging topic and verifying the result…",
            "Rebasing onto main and verifying the result…", "Switching branch…", "Creating branch…", "Moving to the commit…",
            "Deleting branch…", "Deleting branch…", "Creating tag…", "Deleting tag…", "Reverting the commit…", "Cherry-picking the commit…", "Finishing the revert…", "Aborting the revert and verifying the result…",
            "Pushing the tag…", "Deleting the tag on the remote…",
            "Finishing merge and verifying both parents…", "Committing staged content…", "Amending the last commit…",
            "Resetting the branch…", "Adding the remote…", "Saving the remote…", "Removing the remote…", "Staging the hunk…", "Unstaging the hunk…", "Discarding the hunk…", "Staging the lines…", "Unstaging the lines…", "Discarding the lines…", "Editing the commits…", "Ignoring the file…", "Restoring the file…", "Undoing the file's changes…", "Updating submodules…", "Downloading Git LFS files…"],
            requests.Select(WriteText.Progress));
    }

    // A signing key may wait for its passphrase in a window of its own, so the line says what Git waits for.
    [Fact]
    public void WhenACommitIsSigned_ShouldSayTheKeyMayAskForItsPassphrase()
    {
        var signed = Snapshots.Create() with { Signing = CommitSigning.OpenPgp };
        Assert.All(new[] { new CommitRequest(signed, "m"), new CommitRequest(signed, "m", Amend: true) },
            request => Assert.Equal("Signing the commit… Your key may ask for its passphrase.", WriteText.Progress(request)));
    }

    // A merge, a rebase, a revert or a cherry-pick makes commits too, and waits for a passphrase as long as a commit does;
    // so does a tag Git signs. Unsigned, the line stays as it was.
    [Fact]
    public void WhenAnotherWriteMakesSignedCommitsOrASignedTag_ShouldSayTheKeyMayAskForItsPassphrase()
    {
        var signed = Snapshots.Create() with { Signing = CommitSigning.Ssh, SignsTags = true, SigningKey = CommitSigning.Ssh };
        GitWriteRequest[] requests =
        [
            new MergeRequest(signed, "feature", Snapshots.Id('f'), false, "Merge branch 'feature'"), new RebaseRequest(signed, "main", Snapshots.Id('m')),
            new RebaseContinueRequest(signed, "m"), new RebaseSkipRequest(signed), new RewriteRequest(signed, null, []),
            new RevertRequest(signed, Snapshots.Id('a')), new CherryPickRequest(signed, Snapshots.Id('a')), new PickContinueRequest(signed),
            new TagRequest(signed, new("v1", Snapshots.Id('a'), "Release"))
        ];

        Assert.All(requests, request => Assert.EndsWith("… Your key may ask for its passphrase.", WriteText.Progress(request)));
        Assert.Equal("Reverting the commit…", WriteText.Progress(new RevertRequest(Snapshots.Create(), Snapshots.Id('a'))));
        Assert.Equal("Creating tag…", WriteText.Progress(new TagRequest(signed with { SignsTags = false }, new("v1", Snapshots.Id('a'), "Release"))));
    }

    [Fact]
    public void WhenAWriteEndsButTheFinalStatusCannotBeRead_ShouldAskForARefreshBeforeContinuing() =>
        Assert.Equal("Done. The final status could not be read; refresh before continuing.", WriteText.WithoutFinalStatus("Done."));
}
