using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.Application.CherryPick;

// The repository's cherry-picks: a commit of the history applied again by a new commit on the current branch, through
// the repository's writer, and what keeps one back before Git is asked, a commit the branch has already among it. A
// cherry-pick stopped at its conflicts is a pick in progress (PickModel).
public sealed class CherryPickModel(Repository repository, IBranchWorkspace? reader)
{
    // The question asked: the commit to cherry-pick, and whether the current branch has it already, read from Git once
    // per question.
    private HistoryCommit? chosen;
    private bool isHere;

    public Repository Repository => repository;

    // A read that fails decides nothing: the cherry-pick checks again whether the branch has the commit before Git runs.
    public async Task AskAsync(HistoryCommit commit)
    {
        var here = false;
        try
        {
            here = reader is not null && repository.Snapshot is { } snapshot && await reader.ReadContainsAsync(snapshot, commit.Id, CancellationToken.None) == true;
        }
        catch (Exception) { }
        (chosen, isHere) = (commit, here);
    }

    public string Title => chosen is { } commit ? CherryPickText.Title(commit.Id) : "";
    public string Question => chosen is { } commit ? CherryPickText.Question(repository.Snapshot?.Branch, commit.Subject) : "";

    // Why the commit cannot be cherry-picked here, in CherryPickText's words; empty when it can.
    public string Refusal => chosen is { } commit ? CommitRefusals.CherryPick(commit, repository.Snapshot, isHere) : "";
    // An ordinary write with nothing keeping the cherry-pick back.
    public bool CanCherryPick => repository.CanWrite && Refusal.Length == 0;

    public Task<GitWriteResult?> CherryPickAsync(HistoryCommit commit) => repository.Writer.RunAsync(expected => new CherryPickRequest(expected, commit.Id));
}
