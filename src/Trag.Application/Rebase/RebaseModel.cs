using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Branches;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Writes;

namespace Trag.Application.Rebase;

// The repository's rebase. The one in progress, as the snapshot tells it: its plan and what is left of the commit Git
// stopped at, what keeps Continue back (RebaseOverview, worked out once per snapshot over the repository's conflicts), the
// stopped commit's message without Git's conflict list, and the writes that continue, skip or abort it. And one to start:
// what Git says rebasing the current branch onto another would do, read for the branch a dialog shows, and the rebase.
public sealed class RebaseModel
{
    private readonly IBranchWorkspace? reader;
    // What the preview was read for, the branch with its tip; a snapshot, a choice or a tip that changes it reads again.
    private (string? Head, BranchInfo? Onto) previewKey;

    public RebaseModel(Repository repository, IBranchWorkspace? reader)
    {
        Repository = repository;
        this.reader = reader;
        repository.SnapshotApplied += (snapshot, previous) =>
        {
            Overview = new(snapshot, repository.Conflicts.Files);
            Began = repository.HasRebase && snapshot.RebaseIdentity != previous?.RebaseIdentity;
            IsNewStop = snapshot.RebaseStopIdentity != previous?.RebaseStopIdentity;
        };
    }

    public Repository Repository { get; }
    public RebaseOverview Overview { get; private set; } = RebaseOverview.Empty;
    // A rebase began with the latest snapshot, or another took the place of the one before; its next stop is not one.
    public bool Began { get; private set; }
    // The latest snapshot stopped somewhere the snapshot before did not: another commit of the plan, a rebase that began,
    // or one that ended.
    public bool IsNewStop { get; private set; }
    // The message of the commit Git stopped at, without Git's conflict list.
    public string Message => MergeMessages.Clean(Repository.Snapshot?.Rebase?.Message ?? "");
    // A read the repository moved under is read again by the snapshot that moved it, so it has nothing to say itself.
    public Loader<RebasePreview> Preview { get; } = new(RebasePreview.None) { ChangedMessage = "", FailedMessage = Failures.RebasePreviewNotRead };

    // Continue waits for every conflict of the stopped commit and every file staged with markers, and needs an identity
    // and a message.
    public bool CanContinue(string message) =>
        Repository.CanRebase && !Overview.IsBlocked && CommitRequest.CanCommit(Repository.Snapshot, message);

    public Task<GitWriteResult?> ContinueAsync(string message) =>
        CanContinue(message) ? Repository.Writer.RunAsync(expected => new RebaseContinueRequest(expected, message)) : RepositoryWriter.NotRun;

    // Skipping leaves the stopped commit out of the rebased branch; aborting the rebase still brings it back.
    public Task<GitWriteResult?> SkipAsync() => Repository.CanRebase ? Repository.Writer.RunAsync(expected => new RebaseSkipRequest(expected)) : RepositoryWriter.NotRun;

    public Task<GitWriteResult?> AbortAsync() => Repository.CanRebase ? Repository.Writer.RunAsync(expected => new RebaseAbortRequest(expected)) : RepositoryWriter.NotRun;

    // What rebasing onto the chosen branch would do, read over the shown snapshot once the choice is complete.
    public void ReadPreview(RebaseChoice choice)
    {
        var key = (Repository.Snapshot?.Head, choice.Onto);
        if (key == previewKey && Preview.Value.IsRead) return;
        previewKey = key;
        if (reader is null || Repository.Snapshot is not { } snapshot || choice is not { IsComplete: true, Onto: { } onto })
        {
            Preview.Clear();
            return;
        }
        Preview.LoadAsync(async ct => await reader.ReadRebasePreviewAsync(snapshot, onto.Ref, ct), RebasePreview.None);
    }

    public void ClearPreview()
    {
        Preview.Clear();
        previewKey = default;
    }

    // A rebase a dialog started, and the switch that turns one around: one at a time, busy until it ends.
    public Operation Starting { get; } = new();

    // Rebasing the branch others build on onto an older one is almost always the wrong way round: the repository switches
    // to that branch first, whose snapshot reads the branches again, and waits for that read, so the caller can offer
    // to rebase it onto the one that was checked out. Null once switched; otherwise why it did not.
    public Task<string?> TurnAroundAsync(BranchInfo onto) => Starting.RunAsync<string>(async _ =>
    {
        var switched = await Repository.Branches.WriteAsync(BranchAction.Switch, new BranchOperation(onto.Name));
        if (switched is not { Outcome: GitWriteOutcome.Succeeded }) return switched?.Message ?? Failures.SwitchNotStarted(onto.Name);
        await Repository.Branches.List.Completion;
        return null;
    });

    // The rebase a dialog confirmed, onto the commit its preview was read for.
    public Task<GitWriteResult?> StartAsync(BranchInfo onto, string ontoHead) =>
        Starting.RunAsync(_ => Repository.Writer.RunAsync(expected => new RebaseRequest(expected, onto.Name, ontoHead)));
}
