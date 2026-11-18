using System.Globalization;
using System.Text;
using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// What an operation in progress has decided, read with every snapshot while it runs. `git add` of a conflict leaves Git's
// resolve-undo record of both sides in the index; comparing the staged blob with them says whether the user kept ours,
// took theirs or wrote something else. A record whose theirs is not the incoming commit's blob belongs to an older merge
// and is skipped. What was written by hand is looked into for markers, as long as the file's attribute makes them. Open
// conflicts are counted in their working files.
internal sealed class MergeResolutionReader(PresetGitRunner git)
{
    private readonly ConflictMarkerSizes markers = new(git);

    // Without the incoming commit (conflicts a stash left) only the open conflicts are counted: nothing tells the
    // resolve-undo records of these conflicts from older ones.
    public async Task<MergeResolutions> ReadAsync(string root, string? incoming, IReadOnlyList<FileStatus> files, CancellationToken ct)
    {
        var resolved = incoming is null ? [] : await ReadResolvedAsync(root, incoming, ct).ConfigureAwait(false);
        var open = new List<OpenConflict>();
        var conflicts = files.Where(file => file.IsConflict).ToArray();
        var sizes = await markers.ReadAsync(root, conflicts.Where(file => !MergeConflict.DeletesOnASide(file)).Select(file => file.Path), ct).ConfigureAwait(false);
        // A side that deleted the file makes it a keep-or-delete question, never blocks of text.
        foreach (var file in conflicts)
            open.Add(new(file.Path, MergeConflict.DeletesOnASide(file) ? null
                : await BlocksAsync(root, file.Path, ConflictMarkerSizes.Of(sizes, file.Path), ct).ConfigureAwait(false)));
        return new(resolved, open);
    }

    public async Task<IReadOnlyList<ResolvedPath>> ReadResolvedAsync(string root, string incoming, CancellationToken ct)
    {
        var sides = await ReadSidesAsync(root, ct).ConfigureAwait(false);
        if (sides.Count == 0) return [];
        // Per path: the incoming commit's blob, then the staged one.
        var queries = sides.SelectMany(side => new[] { incoming + ":" + side.Name, ":0:" + side.Name }).ToArray();
        var blobs = await BatchCheckAsync(root, queries, ct).ConfigureAwait(false);
        // Our side renamed the file and theirs changed it: Git keeps the conflict under our name, with their side taken
        // from a name only the incoming commit has.
        var renamed = Enumerable.Range(0, sides.Count).Any(i => blobs[2 * i] is null && sides[i].Theirs is not null)
            ? await ReadIncomingOnlyAsync(root, incoming, ct).ConfigureAwait(false) : [];
        var paths = new List<(GitPath Path, Resolution How, Blob? Staged, bool Reopens)>();
        for (var i = 0; i < sides.Count; i++)
        {
            var (side, theirsNow, staged) = (sides[i], blobs[2 * i]?.Id, blobs[2 * i + 1]);
            if (theirsNow != side.Theirs && (theirsNow is not null || !renamed.Contains(side.Theirs!))) continue;
            var how = MergeResolutions.How(side.Mine, side.Theirs, staged?.Id);
            paths.Add((side.Path, how, staged, side.Mine is not null && side.Theirs is not null));
        }
        var edited = paths.Where(path => path.How == Resolution.Edited && path.Staged is not null).ToArray();
        var sizes = await markers.ReadAsync(root, edited.Select(path => path.Path), ct).ConfigureAwait(false);
        var looked = edited.Select(path => new Looked(path.Staged!, ConflictMarkerSizes.Of(sizes, path.Path))).Distinct().ToArray();
        var found = await BlobsWithMarkersAsync(root, looked, ct).ConfigureAwait(false);
        return paths.Select(path => new ResolvedPath(path.Path, path.How,
            path.Staged is not null && found.Contains(new(path.Staged, ConflictMarkerSizes.Of(sizes, path.Path))), path.Reopens)).ToArray();
    }

    private sealed record Blob(string Id, long Size);

    // A staged blob looked into for markers of the length its path's attribute makes.
    private sealed record Looked(Blob Blob, int MarkerSize);

    private sealed record Sides(GitPath Path, string Name, string? Mine, string? Theirs);

    // `ls-files --resolve-undo -z`: "mode id stage\tpath" per side a conflict had, grouped by path in index order.
    private async Task<IReadOnlyList<Sides>> ReadSidesAsync(string root, CancellationToken ct)
    {
        var output = (await git.RunAsync(root, ["ls-files", "--resolve-undo", "-z"], ct).ConfigureAwait(false)).StandardOutput;
        var sides = new List<Sides>();
        foreach (var record in NulRecordReader.Records(output))
        {
            var tab = Array.IndexOf(record, (byte)'\t');
            if (tab < 0) throw new FormatException("Invalid resolve-undo record.");
            var fields = Encoding.ASCII.GetString(record, 0, tab).Split(' ');
            if (fields.Length != 3) throw new FormatException("Invalid resolve-undo record.");
            var path = new GitPath(record[(tab + 1)..]);
            // A path that is not its own text cannot be asked about by name; it stays out of the list.
            if (!path.IsDisplayable) continue;
            if (sides.Count == 0 || !sides[^1].Path.IsSame(path)) sides.Add(new(path, path.Display, null, null));
            sides[^1] = fields[2] switch { "2" => sides[^1] with { Mine = fields[1] }, "3" => sides[^1] with { Theirs = fields[1] }, _ => sides[^1] };
        }
        return sides;
    }

    // The blobs of the files the incoming commit has under a name HEAD does not. `diff-tree -z --raw` writes a record
    // ":000000 <mode> <zero id> <id> A" and then the path, for each.
    private async Task<HashSet<string>> ReadIncomingOnlyAsync(string root, string incoming, CancellationToken ct)
    {
        var output = (await git.RunAsync(root, ["diff-tree", "-r", "-z", "--no-renames", "--diff-filter=A", "HEAD", incoming], ct).ConfigureAwait(false)).StandardOutput;
        return NulRecordReader.Records(output).Where((_, index) => index % 2 == 0).Select(record => Encoding.ASCII.GetString(record).Split(' ')[3]).ToHashSet();
    }

    // One blob per query, null where Git has none.
    private async Task<Blob?[]> BatchCheckAsync(string root, string[] queries, CancellationToken ct)
    {
        var input = Encoding.UTF8.GetBytes(string.Concat(queries.Select(query => query + "\0")));
        var output = (await git.RunAsync(root, ["cat-file", "--batch-check", "-Z"], ct, input).ConfigureAwait(false)).StandardOutput;
        var records = NulRecordReader.Records(output).Select(record => Encoding.UTF8.GetString(record).Split(' ')).ToArray();
        if (records.Length != queries.Length) throw new FormatException("Invalid object check.");
        // "<id> blob <size>" for a blob; "<query> missing" (or a tree, a submodule) is no blob to compare.
        return records.Select(fields => fields is [var id, "blob", var size]
            && long.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) ? new Blob(id, bytes) : null).ToArray();
    }

    // The blobs are read in batches of at most what the merge view reads, so many large files never add up past the
    // runner's limit; a blob larger than that is searched as Git streams it.
    private async Task<HashSet<Looked>> BlobsWithMarkersAsync(string root, Looked[] blobs, CancellationToken ct)
    {
        var found = new HashSet<Looked>();
        foreach (var looked in blobs.Where(looked => looked.Blob.Size > ConflictText.ByteLimit))
            if (await git.StreamAsync(root, ["cat-file", "blob", looked.Blob.Id], (output, token) => ConflictFileText.CountMarkersAsync(output, looked.MarkerSize, token), ct).ConfigureAwait(false) > 0)
                found.Add(looked);
        foreach (var batch in Batches(blobs.Where(looked => looked.Blob.Size <= ConflictText.ByteLimit)))
        {
            var output = (await git.RunAsync(root, ["cat-file", "--batch", "-Z"], ct, Encoding.ASCII.GetBytes(string.Concat(batch.Select(looked => looked.Blob.Id + "\0")))).ConfigureAwait(false)).StandardOutput;
            var offset = 0;
            foreach (var looked in batch)
            {
                var end = Array.IndexOf(output, (byte)0, offset);
                if (end < 0) throw new FormatException("Invalid object batch.");
                var header = Encoding.ASCII.GetString(output, offset, end - offset).Split(' ');
                if (header.Length != 3 || !int.TryParse(header[2], NumberStyles.None, CultureInfo.InvariantCulture, out var size)) throw new FormatException("Invalid object batch.");
                if (await ConflictFileText.CountMarkersAsync(new MemoryStream(output, end + 1, size), looked.MarkerSize, ct).ConfigureAwait(false) > 0) found.Add(looked);
                offset = end + 1 + size + 1;
            }
        }
        return found;
    }

    private static IEnumerable<List<Looked>> Batches(IEnumerable<Looked> blobs)
    {
        var batch = new List<Looked>();
        foreach (var looked in blobs)
        {
            if (batch.Count > 0 && batch.Sum(added => added.Blob.Size) + looked.Blob.Size > ConflictText.ByteLimit)
            {
                yield return batch;
                batch = [];
            }
            batch.Add(looked);
        }
        if (batch.Count > 0) yield return batch;
    }

    private static async Task<int?> BlocksAsync(string root, GitPath path, int markerSize, CancellationToken ct)
    {
        if (!path.IsDisplayable) return null;
        var text = await ConflictFileText.ReadAsync(root, path.Display, markerSize, ct).ConfigureAwait(false);
        if (text.IsReadable) return ConflictDocument.Parse(text.Text, text.MarkerSize)?.Blocks.Count;
        // Text the view does not show still has its markers counted, so it is not marked resolved while they are left; a
        // file without any, binary to Git, has no blocks to wait for.
        return await ConflictFileText.CountMarkersAsync(root, path.Display, markerSize, ct).ConfigureAwait(false) is > 0 and var blocks ? blocks : null;
    }
}
