using Trag.Application.Live;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.Application.Revert;

// The repository's reverts: a commit of the history undone by a new commit on the current branch, through the
// repository's writer, and what keeps one back before Git is asked; the question is about the commit asked last. A
// revert stopped at its conflicts is a pick in progress (PickModel).
public sealed class RevertModel(Repository repository)
{
    // The commit the question is about.
    private HistoryCommit? chosen;

    public Repository Repository => repository;

    public void Ask(HistoryCommit commit) => chosen = commit;

    public string Title => chosen is { } commit ? RevertText.Title(commit.Id) : "";
    public string Question => chosen is { } commit ? RevertText.Question(repository.Snapshot?.Branch, commit.Subject) : "";

    // Why the commit cannot be reverted here, in RevertText's words; empty when it can.
    public string Refusal => chosen is { } commit ? CommitRefusals.Revert(commit, repository.Snapshot) : "";
    // An ordinary write with nothing keeping the revert back.
    public bool CanRevert => repository.CanWrite && Refusal.Length == 0;

    public Task<GitWriteResult?> RevertAsync(HistoryCommit commit) => repository.Writer.RunAsync(expected => new RevertRequest(expected, commit.Id));
}
