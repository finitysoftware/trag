using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Writes;

namespace Trag.Application.Rebase;

// The repository's rewrites: the current branch's commits edited from one of them, through the repository's writer. What
// such an edit would take is read from Git once per question, with how many of the commits the upstream already has;
// what keeps it back follows the repository, and the draft of what happens to the commits is edited until Git is asked.
public sealed class RewriteModel(Repository repository, IBranchWorkspace? reader)
{
    private HistoryCommit? chosen;
    private bool unread;

    public Repository Repository => repository;
    // The commits from the chosen one to the tip; null when nothing can read them or the branch does not have it.
    public RewriteRange? Range { get; private set; }
    public RewriteDraft Draft { get; private set; } = RewriteDraft.Of([]);
    public string Warning { get; private set; } = "";
    public string Title => RewriteText.Title(repository.Snapshot?.Branch ?? "");
    // How many commits the edit takes, from the oldest; empty when none.
    public string Subtitle => Range is { Steps: [var oldest, ..] } range ? RewriteText.Subtitle(range.Steps.Count, oldest.Id) : "";
    // Why the commits cannot be edited as drafted, in RewriteText's words; empty when they can.
    public string Refusal => unread ? RewriteText.Unreadable
        : chosen is not null && CommitRefusals.Rewrite(chosen, repository.Snapshot, Range) is { Length: > 0 } blocked ? blocked : Draft.Refusal;
    // An ordinary write with nothing keeping the edit back.
    public bool CanRewrite => repository.CanWrite && Refusal.Length == 0;

    // A long history can be more than Git's output limit or the read's time allows; the question then says the commits
    // could not be read, and offers no start.
    public async Task OpenAsync(HistoryCommit commit)
    {
        RewriteRange? range = null;
        unread = false;
        try
        {
            range = reader is not null && repository.Snapshot is { } snapshot ? await reader.ReadRewriteAsync(snapshot, commit.Id, CancellationToken.None) : null;
        }
        catch (Exception) { unread = true; }
        (chosen, Range, Warning, Draft) = (commit, range, range is null ? "" : Pushed(range), RewriteDraft.Of(range?.Steps ?? []));
    }

    public void Move(int from, int to) => Draft = Draft.Moved(from, to);
    public void Set(int index, RewriteAction action) => Draft = Draft.With(index, action);
    public void Edit(int index, string message) => Draft = Draft.WithMessage(index, message);

    // The newest commits the upstream lacks are the branch's own; the commits edited beyond those the upstream has.
    private string Pushed(RewriteRange range) =>
        repository.Remotes.Overview.State is { } state && state.PushedAmong(range.Steps.Count) is > 0 and var pushed
            ? RewriteText.Pushed(pushed, state.UpstreamName) : "";

    public Task<GitWriteResult?> RewriteAsync() =>
        Range is { } range ? repository.Writer.RunAsync(expected => new RewriteRequest(expected, range.Base, Draft.Plan)) : RepositoryWriter.NotRun;
}
