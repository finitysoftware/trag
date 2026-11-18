using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.Application.Status;

// Amending the last commit: whether there is one to replace now, its message to start from, what replacing it means
// for the upstream, and the write, a commit that replaces HEAD with the message and whatever is staged on top. An amend
// in progress keeps the message it put aside and the HEAD it is about; it ends once that HEAD moves, being about the last
// commit no more, once the amend is written, and at once when the last commit's message cannot be read.
public sealed partial class AmendModel : ObservableObject
{
    private readonly Repository repository;
    private readonly IHistoryWorkspace? reader;
    private readonly StatusBarState statusBar;
    private string? head;

    public AmendModel(Repository repository, IHistoryWorkspace? reader, StatusBarState statusBar)
    {
        this.repository = repository;
        this.reader = reader;
        this.statusBar = statusBar;
        repository.SnapshotApplied += (snapshot, _) => { if (IsAmending && snapshot.Head != head) End(); };
        repository.Writer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RepositoryWriter.LastWrite) && repository.Writer.LastWrite is { EndsAmend: true }) End();
        };
    }

    [ObservableProperty] public partial bool IsAmending { get; private set; }
    // The author of the commit being replaced, which Git keeps, once its message has arrived.
    [ObservableProperty] public partial GitIdentity? Author { get; private set; }
    // The message being written when the amend began, for when it ends.
    public string Draft { get; private set; } = "";

    // A commit to replace and an ordinary write; nothing has to be staged, a new message alone is an amend.
    public bool CanOffer => repository.CanWrite && repository.Snapshot is { IsUnborn: false };
    public bool CanAmend(string message) => CanOffer && CommitRequest.CanCommit(repository.Snapshot, message);

    // The upstream has the commit already, so a push will have to replace it there; empty when it has not.
    public string Warning => repository.Remotes.Overview.State is { UpstreamHasHead: true } state ? CommitText.AmendPushed(state.UpstreamName) : "";

    // Puts the message being written aside and answers the last commit's message to start from; null when it cannot be
    // read, which ends the amend and says so, or when the amend ended or moved to another HEAD meanwhile.
    public async Task<string?> BeginAsync(string message)
    {
        var started = repository.Snapshot?.Head;
        (head, Draft) = (started, message);
        IsAmending = true;
        var last = await ReadLastCommitAsync();
        if (!IsAmending || head != started) return null;
        if (last is null)
        {
            End();
            statusBar.Notice = CommitText.AmendMessageNotRead;
            return null;
        }
        Author = new(last.Commit.Author, last.Commit.Email);
        return last.Message;
    }

    // The last commit, or null when there is none to read or the read failed.
    private async Task<CommitDetails?> ReadLastCommitAsync()
    {
        if (reader is null || repository.Snapshot is not { Head: { } tip } snapshot) return null;
        try
        {
            return await reader.ReadCommitAsync(snapshot, tip, CancellationToken.None);
        }
        catch (Exception) { return null; }
    }

    public void End() => (IsAmending, Author) = (false, null);

    public Task<GitWriteResult?> AmendAsync(string message) => repository.Writer.RunAsync(expected => new CommitRequest(expected, message, Amend: true));
}
