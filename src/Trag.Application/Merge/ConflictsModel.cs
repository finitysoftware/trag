using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Application.Merge;

// The repository's conflicts, of a merge, a rebase stop or anything else that left them: the files in the two lists their
// view shows (MergeFiles) and where they are resolved, with the sides named that way (ConflictScope), both worked out once
// per snapshot before any screen follows it; the blocks of the conflict a screen shows, read from its working file when it
// is shown and again with every snapshot, so an edit made in the editor shows; and the writes that resolve a whole file or
// one block, or bring a resolved conflict back, each through the repository's writer.
public sealed class ConflictsModel
{
    private readonly ILiveRepositoryWorkspace? reader;

    public ConflictsModel(Repository repository, ILiveRepositoryWorkspace? reader)
    {
        Repository = repository;
        this.reader = reader;
        repository.SnapshotApplied += (snapshot, _) => Follow(snapshot);
    }

    public Repository Repository { get; }
    public MergeFiles Files { get; private set; } = MergeFiles.Empty;
    public ConflictScope Scope { get; private set; } = ConflictScope.None;
    public ConflictSides Sides => Scope.Sides;
    // The conflict whose blocks are read, and the blocks. A read the repository moved under is read again by the snapshot
    // that moved it, so it has nothing to say itself.
    public MergeConflict? Shown { get; private set; }
    public Loader<ConflictView> Document { get; } = new(ConflictView.None) { ChangedMessage = "", FailedMessage = Failures.ConflictNotRead };

    // Resolving is the same during a merge or a rebase. Marking a file resolved waits for its last block, and a block is
    // taken in the file as it was read, so the shown file needs the hash it was read with.
    public bool CanResolve => Repository.CanResolve;
    public bool CanStage(MergeConflict conflict) => CanResolve && conflict.CanMarkResolved;
    public bool CanReopen(MergeResolved file) => CanResolve && file.CanReopen;
    public bool CanTake(ConflictPart part) => part.IsBlock && CanResolve && Shown is not null && Document.Value.Hash.Length != 0;

    // A screen shows this conflict. The same file needs no read of its own: the snapshot that brought it read it again.
    public void Show(MergeConflict? conflict)
    {
        if (conflict is not null && conflict.File.Path.IsSame(Shown?.File.Path)) Shown = conflict;
        else Read(conflict);
    }

    public Task<GitWriteResult?> StageAsync(MergeConflict conflict) =>
        CanStage(conflict) ? Repository.Writer.RunAsync(expected => new StageRequest(expected, [conflict.File.Path], Resolution: true)) : RepositoryWriter.NotRun;

    // A side is written in Git's terms: yours and the other side are Git's ours and theirs in a merge, and the other way
    // round in a rebase.
    public Task<GitWriteResult?> UseAsync(GitPath path, ConflictSide side) =>
        CanResolve ? Repository.Writer.RunAsync(expected => new ConflictSideRequest(expected, path, side.Git)) : RepositoryWriter.NotRun;

    // The user's own tool: the configured editor, or the merge tool.
    public Task<GitWriteResult?> OpenToolAsync(GitPath path, bool editor) =>
        CanResolve ? Repository.Writer.RunAsync(expected => new MergeToolRequest(expected, path, editor)) : RepositoryWriter.NotRun;

    // Bringing a decided file back as a conflict: one the user edited by hand would lose that edit, so it waits for the
    // user; any other comes back at once. True when the user has to confirm; the confirmed one is ReopenAsync.
    public async Task<bool> RequestReopenAsync(MergeResolved file)
    {
        if (!CanReopen(file)) return false;
        if (file.How == Resolution.Edited) return true;
        await ReopenAsync(file);
        return false;
    }

    public Task<GitWriteResult?> ReopenAsync(MergeResolved file) =>
        CanReopen(file) ? Repository.Writer.RunAsync(expected => new ReopenConflictRequest(expected, file.Path)) : RepositoryWriter.NotRun;

    // A block is written in Git's terms too: which side's lines, or both.
    public Task<GitWriteResult?> TakeAsync(ConflictPart part, ConflictChoice choice)
    {
        if (!CanTake(part)) return RepositoryWriter.NotRun;
        var (path, hash) = (Shown!.File.Path, Document.Value.Hash);
        return Repository.Writer.RunAsync(expected => new ConflictBlockRequest(expected, path, part.Block, choice, hash));
    }

    // The shown conflict is read again as the snapshot has it; one that is no longer a conflict is not shown any more.
    private void Follow(RepositorySnapshot snapshot)
    {
        Files = MergeFiles.From(snapshot);
        Scope = ConflictScope.For(snapshot, Files);
        if (Shown is { } shown) Read(Files.Conflicts.FirstOrDefault(conflict => conflict.File.Path.IsSame(shown.File.Path)));
    }

    // Another file starts from an empty view; the same file read again keeps its blocks until the new ones arrive.
    private void Read(MergeConflict? conflict)
    {
        var other = conflict is null || !conflict.File.Path.IsSame(Shown?.File.Path);
        Shown = conflict;
        if (reader is null || conflict is null)
        {
            Document.Clear();
            return;
        }
        // A side that deleted the file leaves nothing to show but the question whether it stays.
        if (conflict.IsDeletion)
        {
            var question = new ConflictView([], "", MergeText.DeletionChoice(conflict, Sides));
            Document.LoadAsync(_ => Task.FromResult<ConflictView?>(question));
            return;
        }
        Document.LoadAsync(async ct => await reader.ReadConflictAsync(conflict.File, ct) is { } text ? ConflictView.From(text) : null,
            other ? ConflictView.None : null);
    }
}
