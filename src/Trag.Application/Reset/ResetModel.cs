using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain;
using Trag.Domain.History;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Application.Reset;

// The repository's resets: the current branch moved back to one of its own commits, through the repository's writer.
// How far it goes back is read from Git once per question; what keeps a reset back, what a hard one loses and which
// leaving commits the upstream has follow the repository and are said before Git is asked.
public sealed class ResetModel(Repository repository, IBranchWorkspace? reader)
{
    // The question asked: the commit the branch goes back to, how many commits leave it (null when the branch does not
    // have the commit), and the HEAD that was counted from.
    private HistoryCommit? chosen;
    private int? distance;
    // The commit's files the index lacks when the question was asked: a commit does not change, so whichever untracked
    // file stands in their way is found against the working tree as it is now.
    private IReadOnlyList<GitPath>? notInIndex;
    private string? askedOn;
    private bool unread;

    public Repository Repository => repository;

    // A read that fails, past its time limit on a long history or with Git gone, leaves a question that says so.
    public async Task AskAsync(HistoryCommit commit)
    {
        var head = repository.Snapshot?.Head;
        int? far = null;
        IReadOnlyList<GitPath>? missing = null;
        unread = false;
        try
        {
            if (reader is not null && repository.Snapshot is { } snapshot)
            {
                far = await reader.ReadDistanceAsync(snapshot, commit.Id, CancellationToken.None);
                missing = await reader.ReadNotInIndexAsync(snapshot, commit.Id, CancellationToken.None);
            }
        }
        catch (Exception) { unread = true; }
        (chosen, distance, askedOn, notInIndex) = (commit, far, head, missing);
    }

    public string Title => chosen is { } commit ? ResetText.Title(repository.Snapshot?.Branch ?? "", GitObjectId.Short(commit.Id)) : "";

    // Why the branch cannot go back to the commit, in ResetText's words; empty when it can.
    public string Refusal => chosen is not { } commit ? ""
        : unread ? ResetText.DistanceUnreadable(repository.Snapshot?.Branch ?? "")
        : CommitRefusals.Reset(commit, repository.Snapshot, distance, askedOn);
    // An ordinary write with nothing keeping the reset back.
    public bool CanReset => repository.CanWrite && Refusal.Length == 0;

    public string Summary => chosen is { } commit && distance is { } far
        ? ResetText.Summary(repository.Snapshot?.Branch ?? "", far, GitObjectId.Short(repository.Snapshot?.Head ?? ""), GitObjectId.Short(commit.Id)) : "";

    // The uncommitted work a hard reset takes with it: every tracked change, and the untracked files the commit's files
    // take the place of; other untracked files stay.
    public string Loss => ResetText.HardLoss(repository.Snapshot?.Files.Count(file => !file.IsUntracked) ?? 0,
        OverwrittenUntracked.Of(repository.Snapshot?.Files ?? [], notInIndex ?? []));

    // The newest commits the upstream lacks are the branch's own; leaving commits beyond those the upstream has. A refused
    // reset has none: no distance off the branch, none to its tip, and no upstream on a detached HEAD.
    public string Pushed =>
        distance is { } far && repository.Remotes.Overview.State is { } state && state.PushedAmong(far) is > 0 and var pushed
            ? ResetText.Pushed(pushed, state.UpstreamName) : "";

    public Task<GitWriteResult?> ResetAsync(HistoryCommit commit, ResetMode mode) =>
        repository.Writer.RunAsync(expected => new ResetRequest(expected, commit.Id, mode));
}
