using System.Text;
using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Processes;
using Trag.Domain;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.Git.Read;

// One commit with the files it changed. The file list is streamed and stops at the limit with a notice, and repeated
// modes and object IDs are shared, because a large commit repeats the same handful of strings thousands of times.
internal sealed class CommitDetailsReader(PresetGitRunner git)
{
    public async Task<CommitDetails> ReadCommitAsync(string root, string id, CancellationToken ct)
    {
        GitObjectId.Validate(id);
        try { return await ReadAsync(root, id, signed: true, ct).ConfigureAwait(false); }
        // Git waits for the program that checks the signature, which can wait for good (gpg on its agent or a key server);
        // the commit is then read again without the check, saying the check did not finish.
        catch (TimeoutException) { return await ReadAsync(root, id, signed: false, ct).ConfigureAwait(false); }
    }

    private Task<CommitDetails> ReadAsync(string root, string id, bool signed, CancellationToken ct) => ReadTimeout.RunAsync(TimeSpan.FromSeconds(30), async ct =>
    {
        // Read whole, because what Git says on stderr while it checks the signature is part of the answer. Without
        // --no-renames, show looks for renames even with -s and fails on versions a partial clone did not download.
        var shown = await git.RunAsync(root, ["show", "-s", "--no-renames", "--no-notes", "--no-show-signature", "--encoding=UTF-8", "-z", signed ? CommitRecord.SignedFormat : CommitRecord.Format, id, "--"], ct).ConfigureAwait(false);
        var record = await new GitRecordStream(new MemoryStream(shown.StandardOutput)).ReadAsync(ct, signed ? CommitRecord.SignedFields : CommitRecord.Fields).ConfigureAwait(false) ?? throw new FormatException("Missing commit.");
        var (entry, signature) = signed ? CommitRecord.ParseSigned(record.Span, Encoding.UTF8.GetString(shown.StandardError)) : (CommitRecord.Parse(record.Span), new CommitSignature(SignatureCheck.NotFinished, ""));
        // Git shows the oldest commit of a shallow clone without a parent; only the commit object still names it.
        var details = new CommitDetails(entry.Summary, entry.Message, [])
        {
            Signature = signature,
            IsShallowBoundary = entry.Summary.Parents.Count == 0
                && CommitObject.NamesParent((await git.RunAsync(root, ["cat-file", "commit", id], ct).ConfigureAwait(false)).StandardOutput)
        };
        IReadOnlyList<FileDiffMetadata> files;
        // Rename detection compares the content of every deleted file with every added one, which a partial clone may not
        // have downloaded: the list is then read again as the deletions and additions Git records.
        try { files = await ReadFilesAsync(root, details, ["--find-renames=50%", "-l1000"], ct).ConfigureAwait(false); }
        catch (GitCommandException) { files = await ReadFilesAsync(root, details, ["--no-renames"], ct).ConfigureAwait(false); }
        return details with { Files = files, FileNotice = files.Count == HistoryLimits.FileLimit ? CommitText.FileLimitNotice : "" };
    }, ct);

    private Task<IReadOnlyList<FileDiffMetadata>> ReadFilesAsync(string root, CommitDetails commit, string[] renames, CancellationToken ct) =>
        git.ReadStreamAsync(root, CommitDiffArguments.For(commit, ["--raw", "-z", "--no-abbrev", .. renames]),
            async (stream, token) =>
            {
                var records = new GitRecordStream(stream);
                var files = new List<FileDiffMetadata>();
                var shared = new Dictionary<string, string>(StringComparer.Ordinal);
                while (await records.ReadAsync(token).ConfigureAwait(false) is { } header)
                {
                    if (files.Count == HistoryLimits.FileLimit) return ((IReadOnlyList<FileDiffMetadata>)files, false);
                    files.Add(await ReadFileAsync(records, header, shared, token).ConfigureAwait(false));
                }
                return ((IReadOnlyList<FileDiffMetadata>)files, true);
            }, ct);

    // One raw record: the header with modes, objects and status, then the path, and for a rename or copy the
    // destination after the source. The header is read before the next record is asked for, because it is borrowed.
    private static async ValueTask<FileDiffMetadata> ReadFileAsync(GitRecordStream records, ReadOnlyMemory<byte> header,
        Dictionary<string, string> shared, CancellationToken ct)
    {
        var fields = FileDiffParser.RawHeader(header.Span);
        GitObjectId.Validate(fields[2]);
        GitObjectId.Validate(fields[3]);
        var path = new GitPath((await records.ReadAsync(ct).ConfigureAwait(false) ?? throw new FormatException("Missing path.")).ToArray());
        GitPath? original = null;
        if (fields[4].StartsWith('R') || fields[4].StartsWith('C'))
        {
            original = path;
            path = new((await records.ReadAsync(ct).ConfigureAwait(false) ?? throw new FormatException("Missing destination.")).ToArray());
        }
        return new(path, Share(fields[4]), Share(fields[0]), Share(fields[1]), Share(fields[2]), Share(fields[3]), original);

        string Share(string value)
        {
            if (shared.TryGetValue(value, out var found)) return found;
            if (shared.Count < 4096) shared.Add(value, value);
            return value;
        }
    }
}
