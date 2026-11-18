using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Parsing;

// The two reads that tell which paths are Git LFS's, taken as the output arrives, because a large repository's index
// or tree is more than the runner holds: the paths of ls-files --stage or ls-tree records, joined as check-attr --stdin
// -z takes them, and check-attr's answer, each path with the attribute and its value.
internal static class LfsFilterRecords
{
    public static async Task<byte[]> PathsAsync(Stream output, CancellationToken ct)
    {
        var records = new GitRecordStream(output);
        using var paths = new MemoryStream();
        while (await records.ReadAsync(ct) is { } row)
        {
            var separator = row.Span.IndexOf((byte)'\t');
            if (separator < 0) throw new FormatException("Invalid path record.");
            paths.Write(row.Span[(separator + 1)..]);
            paths.WriteByte(0);
        }
        return paths.ToArray();
    }

    public static async Task<int> CountLfsAsync(Stream output, CancellationToken ct)
    {
        var records = new GitRecordStream(output);
        var lfs = 0;
        while (await records.ReadAsync(ct, fields: 3) is { } answer)
            if (GitEntries.IsLfs(answer.Span[(answer.Span.LastIndexOf((byte)0) + 1)..])) lfs++;
        return lfs;
    }
}
