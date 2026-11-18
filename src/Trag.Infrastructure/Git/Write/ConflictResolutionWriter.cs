using System.Text;
using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// The choices the merge view makes on a conflict. A side for the whole file is Git's own checkout --ours/--theirs and a
// stage with that side's executable bit, or a removal where that side deleted the file; a submodule's side is that side's commit, written into the index; bringing a conflict back is checkout -m, which Git can do for a
// conflict resolved in this merge. Both run against the transaction's private index. A choice on one block rewrites the
// working file itself, only over the bytes it was made on. Marking a file resolved is refused while markers are left, and
// so are Finish and Continue while a conflict is open or a resolved file still holds markers.
internal sealed class ConflictResolutionWriter(WriteCommandRunner git, ExpectedStateCheck snapshots, MergeResolutionReader resolutions, ConflictMarkerSizes markers)
{
    // What keeps Finish and Continue back, asked of the fresh repository: an open conflict, or a staged file that still
    // has conflict markers. The caller names its conflicts sentence and its action.
    public async Task<string?> StillBlockedAsync(RepositorySnapshot fresh, string conflictsLeft, string action, CancellationToken ct) =>
        fresh.Files.Any(file => file.IsConflict) ? conflictsLeft
        : await MarkersLeftAsync(fresh, ct).ConfigureAwait(false) is { } markers
            ? WriteText.WouldCommitMarkers(action, markers) : null;

    // The resolved conflicts of the operation in progress whose staged content still holds markers, named for the
    // refusal; null for none.
    private async Task<string?> MarkersLeftAsync(RepositorySnapshot fresh, CancellationToken ct)
    {
        if (fresh.Theirs is not { } incoming) return null;
        var left = (await resolutions.ReadResolvedAsync(fresh.Root, incoming, ct).ConfigureAwait(false)).Where(path => path.HasMarkers).ToArray();
        return left.Length == 0 ? null : string.Join(", ", left.Select(path => path.Path.DisplayEscaped));
    }

    public async Task<GitWriteResult> UseSideAsync(ConflictSideRequest request, RepositorySnapshot fresh, IndexTransaction transaction, CancellationToken ct)
    {
        // ls-files takes no pathspec file, so the path goes on the command line and has to be its own text. A stage list
        // that cannot be read decides nothing: a missing side would otherwise read as a deletion.
        if (!request.Path.IsDisplayable) return GitWriteResult.Rejected(ConflictOutcomeText.PathNotNameable);
        var paths = GitPathEncoder.Encode([request.Path]);
        var stages = await git.RunAsync(fresh.Root, ["ls-files", "--stage", "-z", "--", request.Path.Display], ct, transaction).ConfigureAwait(false);
        if (stages.ExitCode != 0 || stages.StandardOutput.Length == 0)
            return GitWriteResult.Rejected(ConflictOutcomeText.SidesNotListed, GitOutputText.Details(stages));
        var stage = request.Side == ConflictChoice.Mine ? "2" : "3";
        var chosen = NulRecordReader.Records(stages.StandardOutput).Select(record => Encoding.ASCII.GetString(record).Split('\t')[0].Split(' '))
            .FirstOrDefault(entry => entry is [_, _, var number] && number == stage);
        var kept = chosen is not null;
        // Checkout leaves a submodule as it is and add would stage the commit it has checked out, so the chosen commit goes
        // into the index itself; the submodule's checkout follows with Update submodules. A file is added, which records
        // its stat data (Trag's status never writes it back), and then given the chosen side's executable bit, which add
        // takes from our stage under core.fileMode=false.
        (string[] Arguments, byte[]? Input)[] Mode(string mode) => mode is "100755" or "100644"
            ? [(["update-index", mode == "100755" ? "--chmod=+x" : "--chmod=-x", "--", request.Path.Display], null)] : [];
        (string[] Arguments, byte[]? Input)[] steps = chosen is null ? [(["rm", "--quiet", "--pathspec-from-file=-", "--pathspec-file-nul"], paths)]
            : chosen[0] == "160000" ? [(["update-index", "--cacheinfo", "160000," + chosen[1] + "," + request.Path.Display], null)]
            : [(["checkout", request.Side == ConflictChoice.Mine ? "--ours" : "--theirs", "--pathspec-from-file=-", "--pathspec-file-nul"], paths),
                (["add", "--pathspec-from-file=-", "--pathspec-file-nul"], paths), .. Mode(chosen[0])];
        // The first step rewrites the working file and the index is published last, so a write stopped on the way may have
        // changed a file whose conflict still reads as untouched: it asks for a review.
        try
        {
            foreach (var (arguments, input) in steps)
            {
                var result = await git.RunAsync(fresh.Root, arguments, ct, transaction, input).ConfigureAwait(false);
                if (result.ExitCode != 0) return GitWriteResult.Rejected(ConflictOutcomeText.SideNotTaken, GitOutputText.Details(result));
            }
            return await snapshots.PublishAsync(fresh, transaction, kept ? ConflictOutcomeText.ResolvedWith(request.Path.DisplayEscaped, request.Side) : ConflictOutcomeText.ResolvedAsDeleted(request.Path.DisplayEscaped, request.Side), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return new(GitWriteOutcome.Uncertain, WriteText.Interrupted); }
    }

    public async Task<GitWriteResult> ReopenAsync(ReopenConflictRequest request, RepositorySnapshot fresh, IndexTransaction transaction, CancellationToken ct)
    {
        var result = await git.RunAsync(fresh.Root, ["checkout", "-m", "--pathspec-from-file=-", "--pathspec-file-nul"], ct, transaction, GitPathEncoder.Encode([request.Path])).ConfigureAwait(false);
        if (result.ExitCode != 0)
            return GitWriteResult.Rejected(ConflictOutcomeText.NotReopened, GitOutputText.Details(result));
        return await snapshots.PublishAsync(fresh, transaction, ConflictOutcomeText.InConflictAgain(request.Path.DisplayEscaped), ct).ConfigureAwait(false);
    }

    public async Task<GitWriteResult> TakeBlockAsync(ConflictBlockRequest request, RepositorySnapshot fresh, CancellationToken ct)
    {
        var full = RepositoryPathGuard.SafePath(fresh.Root, request.Path.Display);
        if (RepositoryPathGuard.HasLink(fresh.Root, full) || !File.Exists(full)) return GitWriteResult.Rejected(ConflictOutcomeText.NotPlainFile);
        var bytes = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);
        if (ConflictFileText.Hash(bytes) != request.ContentHash) return GitWriteResult.Rejected(ConflictOutcomeText.FileChanged);
        var text = ConflictFileText.From(bytes, ConflictMarkerSizes.Of(await markers.ReadAsync(fresh.Root, [request.Path], ct).ConfigureAwait(false), request.Path));
        if (!text.IsReadable || ConflictDocument.Parse(text.Text, text.MarkerSize) is not { } document || request.Block >= document.Blocks.Count)
            return GitWriteResult.Rejected(ConflictOutcomeText.ConflictsChanged);
        // Written in place, so the file keeps its permissions; Git sees the new content with the next status. Once the bytes
        // are checked the write goes through whole: a cancel halfway would leave the file cut short.
        await File.WriteAllBytesAsync(full, new UTF8Encoding(false).GetBytes(document.Take(request.Block, request.Choice)), CancellationToken.None).ConfigureAwait(false);
        return new(GitWriteOutcome.Succeeded, ConflictOutcomeText.BlockUses(request.Block + 1, document.Blocks.Count, request.Path.DisplayEscaped, request.Choice));
    }

    // A file still in conflict is only staged as resolved once no conflict block is left in it.
    public async Task<string?> RefuseMarkersAsync(IReadOnlyList<GitPath> paths, RepositorySnapshot fresh, CancellationToken ct)
    {
        var conflicted = paths.Where(path => path.IsDisplayable && fresh.Conflicts.Any(file => file.Path.IsSame(path))).ToArray();
        var sizes = await markers.ReadAsync(fresh.Root, conflicted, ct).ConfigureAwait(false);
        foreach (var path in conflicted)
            if (await ConflictFileText.CountMarkersAsync(fresh.Root, path.Display, ConflictMarkerSizes.Of(sizes, path), ct).ConfigureAwait(false) > 0)
                return ConflictOutcomeText.MarkersLeft(path.DisplayEscaped);
        return null;
    }
}
