using Trag.Application.Ports.Git;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Merge;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Rebase;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// The one object the application asks to change a repository. It decides nothing about Git itself: it validates the
// request, reads again what a write compares, refuses a state it cannot write to, opens the index transaction where
// the writer needs one, and hands the work to the writer that owns that flow. The order of these steps is the contract.
public sealed class GitWriteRouter : IGitWriter
{
    private readonly IGitSyncWriter? synchronizer;
    private readonly WriteStateReader states;
    private readonly ExpectedStateCheck snapshots;
    private readonly BranchWriter branches;
    private readonly TagWriter tags;
    private readonly PickWriter picks;
    private readonly ResetWriter resets;
    private readonly RemoteWriter remotes;
    private readonly IgnoreWriter ignores;
    private readonly FileVersionWriter versions;
    private readonly StashWriter stashes;
    private readonly DiscardWriter discards;
    private readonly CommitWriter commits;
    private readonly MergeAbortWriter aborts;
    private readonly MergeToolLauncher tools;
    private readonly IndexStagingWriter staging;
    private readonly PatchWriter patches;
    private readonly BranchMergeWriter merges;
    private readonly RebaseWriter rebases;
    private readonly ConflictResolutionWriter conflicts;

    // A merge and a rebase read the repository again with the reader the shell holds, which already knows Git's version.
    public GitWriteRouter(GitProcessRunner runner, IGitSyncWriter? synchronizer = null, IRepositoryReader? reader = null)
    {
        this.synchronizer = synchronizer;
        reader ??= new CliRepositoryReader(runner);
        var git = new WriteCommandRunner(runner);
        var read = new PresetGitRunner(runner, LocalGitPreset.Reading);
        var heads = new HeadReader(runner);
        states = new(heads, new GitIdentityReader(read), read);
        snapshots = new(states);
        var refNames = new GitRefNameValidator(runner);
        branches = new(git, heads, refNames, new BranchDeleteWriter(git), new DetachedCheckoutWriter(git, heads));
        tags = new(git, refNames);
        picks = new(git, reader);
        resets = new(git, reader);
        remotes = new(git, reader);
        ignores = new(git);
        versions = new(git);
        stashes = new(git, new StashEntryResolver(git));
        discards = new(git, read);
        commits = new(git, heads, snapshots);
        aborts = new(git, heads);
        tools = new(git, new GitEditorResolver(git), snapshots);
        staging = new(git, snapshots);
        patches = new(git, snapshots);
        merges = new(git, new VerifiedMerge(reader, new PresetGitRunner(runner, LocalGitPreset.Writing)));
        conflicts = new(git, snapshots, new(read), new(read));
        rebases = new(git, reader);
    }

    // Publishing, a transfer, a remote branch deletion, a tag sent to or deleted on a remote, a download of Git LFS content
    // and a submodule update all talk to a remote, so they belong to the synchronizer.
    public Task<GitWriteResult> ExecuteAsync(GitWriteRequest request, CancellationToken ct) =>
        request is SyncRequest or PublishRequest or RemoteBranchDeleteRequest or RemoteTagRequest or LfsDownloadRequest or SubmoduleUpdateRequest
            ? synchronizer?.ExecuteAsync(request, ct) ?? Task.FromResult(new GitWriteResult(GitWriteOutcome.Rejected, WriteText.SyncUnavailable))
            : Task.Run(() => ExecuteBackgroundAsync(request, ct));

    private async Task<GitWriteResult> ExecuteBackgroundAsync(GitWriteRequest request, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (GitWriteRequestValidator.Validate(request) is { } validation) return GitWriteResult.Rejected(validation);
            var fresh = await states.ReadAsync(request, ct).ConfigureAwait(false);
            if (WriteNeeds.Refusal(request, fresh) is { } refused) return GitWriteResult.Rejected(refused);
            // Branches, stashes, discards (a hunk's too), reverts, cherry-picks, resets and rebases run against the real index: Git itself refuses
            // what they would overwrite, and a rebase lives across several commands. A tag or a remote touches no index at all. A merge
            // takes a private index of its own, the same way a pull's merge does; a choice on one conflict block only
            // rewrites the working file.
            // A stash, a discard, a revert, a cherry-pick, a rebase step or a file restored from history that Cancel stops
            // leaves the lock its killed Git held; a switch and a reset release theirs themselves. No other write takes
            // that lock, so one found after it was stopped is another program's.
            var left = HoldsTheIndexLock(request) ? new StoppedWriteLock(Path.Combine(fresh.GitDirectory!, "index")) : null;
            try
            {
                return request switch
                {
                    ConflictBlockRequest block => await conflicts.TakeBlockAsync(block, fresh, ct).ConfigureAwait(false),
                    RebaseRequest rebase => await rebases.StartAsync(rebase, fresh, ct).ConfigureAwait(false),
                    RewriteRequest rewrite => await rebases.RewriteAsync(rewrite, fresh, ct).ConfigureAwait(false),
                    RebaseContinueRequest when await conflicts.StillBlockedAsync(fresh, WriteText.RebaseConflictsLeft, WriteText.ContinueAction, ct)
                            .ConfigureAwait(false) is { } blocked => GitWriteResult.Rejected(blocked),
                    RebaseContinueRequest next => await rebases.ContinueAsync(next, fresh, ct).ConfigureAwait(false),
                    RebaseSkipRequest => await rebases.SkipAsync(fresh, ct).ConfigureAwait(false),
                    RebaseAbortRequest => await rebases.AbortAsync(fresh, ct).ConfigureAwait(false),
                    BranchRequest branch => await branches.BranchAsync(branch, fresh, ct).ConfigureAwait(false),
                    BranchRenameRequest rename => await branches.RenameAsync(rename, fresh, ct).ConfigureAwait(false),
                    TagRequest tag => await tags.CreateAsync(tag, fresh, ct).ConfigureAwait(false),
                    TagDeleteRequest tag => await tags.DeleteAsync(tag, fresh, ct).ConfigureAwait(false),
                    RevertRequest revert => await picks.StartAsync(revert, fresh, ct).ConfigureAwait(false),
                    CherryPickRequest pick => await picks.StartAsync(pick, fresh, ct).ConfigureAwait(false),
                    PickContinueRequest when await conflicts.StillBlockedAsync(fresh, PickText.ConflictsLeft(fresh.Operation), WriteText.FinishAction, ct)
                            .ConfigureAwait(false) is { } blocked => GitWriteResult.Rejected(blocked),
                    PickContinueRequest => await picks.ContinueAsync(fresh, ct).ConfigureAwait(false),
                    PickAbortRequest => await picks.AbortAsync(fresh, ct).ConfigureAwait(false),
                    ResetRequest reset => await resets.ResetAsync(reset, fresh, ct).ConfigureAwait(false),
                    RemoteAddRequest add => await remotes.AddAsync(add, fresh, ct).ConfigureAwait(false),
                    RemoteEditRequest change => await remotes.EditAsync(change, fresh, ct).ConfigureAwait(false),
                    RemoteRemoveRequest remove => await remotes.RemoveAsync(remove, fresh, ct).ConfigureAwait(false),
                    IgnoreRequest ignore => await ignores.IgnoreAsync(ignore, fresh, ct).ConfigureAwait(false),
                    RestoreFileRequest restore => await versions.RestoreAsync(restore, fresh, ct).ConfigureAwait(false),
                    RevertFileRequest revert => await versions.RevertAsync(revert, fresh, ct).ConfigureAwait(false),
                    MergeRequest merge => await merges.MergeAsync(merge, fresh, ct).ConfigureAwait(false),
                    StashRequest stash => await stashes.StashAsync(stash, fresh, ct).ConfigureAwait(false),
                    DiscardRequest discard => await discards.DiscardAsync(discard, fresh, ct).ConfigureAwait(false),
                    PatchRequest { Action: PatchAction.Discard } hunk => await patches.DiscardAsync(hunk, fresh, ct).ConfigureAwait(false),
                    _ => await WriteIndexAsync(request, fresh, ct).ConfigureAwait(false)
                };
            }
            finally { left?.Release(ct); }
        }
        catch (OperationCanceledException) { return new(GitWriteOutcome.Cancelled, WriteText.Canceled); }
        catch (GitOutputLimitException) { return GitWriteResult.Rejected(WriteText.TooMuchOutput); }
        catch (Exception error) when (GitFailures.IsExpected(error)) { return GitWriteResult.Rejected(WriteText.NotCompleted); }
    }

    private static bool HoldsTheIndexLock(GitWriteRequest request) => request is StashRequest or DiscardRequest or RevertRequest
        or CherryPickRequest or PickContinueRequest or PickAbortRequest or RebaseRequest or RewriteRequest or RebaseContinueRequest
        or RebaseSkipRequest or RebaseAbortRequest or RestoreFileRequest;

    // Commits, merge steps and index updates work on a private copy of the index under Trag's own lock, taken only
    // after the repository is confirmed unchanged once more.
    private async Task<GitWriteResult> WriteIndexAsync(GitWriteRequest request, RepositorySnapshot fresh, CancellationToken ct)
    {
        var transaction = IndexTransaction.TryBegin(fresh.GitDirectory!);
        if (transaction is null) return GitWriteResult.Rejected(WriteText.IndexLockHeld);
        await using (transaction)
        {
            if (!await snapshots.StillExpectedAsync(fresh, ct).ConfigureAwait(false)) return GitWriteResult.Rejected(WriteText.ChangedDuring);
            await transaction.CopyAsync(ct).ConfigureAwait(false);
            return request switch
            {
                CommitRequest { FinishesMerge: true } when await conflicts.StillBlockedAsync(fresh, WriteText.MergeConflictsLeft, WriteText.FinishAction, ct)
                        .ConfigureAwait(false) is { } blocked => GitWriteResult.Rejected(blocked),
                CommitRequest commit => await commits.CommitAsync(commit, fresh, transaction, ct).ConfigureAwait(false),
                ConflictSideRequest side => await conflicts.UseSideAsync(side, fresh, transaction, ct).ConfigureAwait(false),
                ReopenConflictRequest reopen => await conflicts.ReopenAsync(reopen, fresh, transaction, ct).ConfigureAwait(false),
                StageRequest { Resolution: true } stage when await conflicts.RefuseMarkersAsync(stage.Paths, fresh, ct).ConfigureAwait(false) is { } refusal
                    => GitWriteResult.Rejected(refusal),
                MergeAbortRequest => await aborts.AbortMergeAsync(fresh, transaction, ct).ConfigureAwait(false),
                MergeToolRequest tool => await tools.OpenMergeToolAsync(tool, fresh, transaction, ct).ConfigureAwait(false),
                StageRequest stage => await staging.StageAsync(stage.Paths, true, fresh, transaction, ct).ConfigureAwait(false),
                UnstageRequest unstage => await staging.StageAsync(unstage.Paths, false, fresh, transaction, ct).ConfigureAwait(false),
                PatchRequest patch => await patches.ApplyAsync(patch, fresh, transaction, ct).ConfigureAwait(false),
                _ => GitWriteResult.Rejected(WriteText.UnknownWrite)
            };
        }
    }
}
