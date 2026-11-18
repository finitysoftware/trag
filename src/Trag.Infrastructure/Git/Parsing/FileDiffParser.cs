using System.Text;
using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;

namespace Trag.Infrastructure.Git.Parsing;

// Git's diff output, read once, line by line, into the rows the diff view shows. Two shapes arrive here: a patch with
// hunks, and the whole content of a file that has nothing to be compared against. Both share the same limits.
public static class FileDiffParser
{
    // --raw -z: a header record, then the path. Every field is kept as Git printed it; the path is kept as bytes.
    public static IReadOnlyList<FileDiffMetadata> Metadata(byte[] bytes)
    {
        var records = NulRecordReader.Records(bytes).ToArray();
        var files = new List<FileDiffMetadata>();
        for (var i = 0; i < records.Length; i += 2)
        {
            if (i + 1 == records.Length) throw new FormatException("Incomplete raw diff.");
            var fields = RawHeader(records[i]);
            files.Add(new(new(records[i + 1]), fields[4], fields[0], fields[1], fields[2], fields[3]));
        }
        return files;
    }

    // ":<old mode> <new mode> <old object> <new object> <status>", the modes without the colon.
    public static string[] RawHeader(ReadOnlySpan<byte> header)
    {
        var fields = Encoding.ASCII.GetString(header).Split(' ');
        if (fields.Length != 5 || !fields[0].StartsWith(':')) throw new FormatException("Invalid raw diff.");
        fields[0] = fields[0][1..];
        return fields;
    }

    public static FileDiff Patch(IReadOnlyList<FileDiffMetadata> metadata, byte[] bytes, bool preview, bool outputTruncated = false)
    {
        var lines = new PatchLines(outputTruncated);
        var hunk = new PatchHunk();
        var headers = new List<string>();
        var content = false;
        foreach (var raw in ReadLines(bytes))
        {
            var text = lines.Clip(raw.Text);
            if (text.StartsWith("Binary files ", StringComparison.Ordinal))
                return new(metadata, [], DiffContentKind.Binary, FileDiffText.BinaryDiff);
            if (text == "\\ No newline at end of file" && lines.RemoveTerminatorOfLast()) continue;
            if (lines.IsFull()) break;
            var line = hunk.ReadLine(text, raw.Cr);
            if (line.IsHeader) headers.Add(line.Text);
            else content = true;
            lines.Add(line);
        }
        return lines.ToDiff(metadata, preview, FileDiffText.LimitedDiff, FileDiffText.LargeDiff, content ? "" : WithoutContent(headers));
    }

    // A patch without a line of content says in its headers what changed: an empty file added or deleted, or the file's
    // mode alone. Anything else has no content change to show, as a file renamed as it was, whose patch names only its old
    // and new path.
    private static string WithoutContent(List<string> headers)
    {
        string? Header(string name) => headers.FirstOrDefault(header => header.StartsWith(name, StringComparison.Ordinal))?[name.Length..];
        var (added, deleted) = (Header("new file mode ") is not null, Header("deleted file mode ") is not null);
        if (added != deleted) return added ? FileDiffText.NewEmptyFile : FileDiffText.EmptyFileDeleted;
        return Header("old mode ") is { } from && Header("new mode ") is { } to ? FileDiffText.ModeChanged(from, to) : FileDiffText.NoChanges;
    }

    // A file with nothing to compare against: every row is an addition, or, for a bounded preview of content that is
    // simply there, a context row numbered on the side the content belongs to.
    public static FileDiff NewContent(IReadOnlyList<FileDiffMetadata> metadata, byte[] bytes, bool preview, bool limited,
        bool contentOnly = false, bool oldContent = false)
    {
        if (bytes.AsSpan().Contains((byte)0)) return new(metadata, [], DiffContentKind.Binary, FileDiffText.BinaryPreview);
        var lines = new PatchLines(limited);
        var kind = contentOnly ? ' ' : '+';
        foreach (var raw in ReadLines(bytes))
        {
            if (lines.IsFull()) break;
            var text = lines.Clip(kind + raw.Text);
            var number = lines.Count + 1;
            lines.Add(new(kind, text, oldContent ? number : null, oldContent ? null : number)
                { HasCarriageReturn = raw.Cr, NoNewline = !lines.Limited && !raw.Terminated });
        }
        return lines.ToDiff(metadata, preview, FileDiffText.LimitedContent, FileDiffText.LargeContent,
            lines.Count == 0 ? FileDiffText.NewEmptyFile : FileDiffText.NewFile);
    }

    // One line at a time, of bytes the readers already bounded. A bare CR inside content is preserved; only CR immediately
    // before LF is an end of line, and the last line says whether it had one at all.
    private static IEnumerable<(string Text, bool Cr, bool Terminated)> ReadLines(byte[] bytes)
    {
        for (var start = 0; start < bytes.Length;)
        {
            var end = Array.IndexOf(bytes, (byte)'\n', start);
            var terminated = end >= 0;
            if (!terminated) end = bytes.Length;
            var cr = terminated && end > start && bytes[end - 1] == '\r';
            yield return (Encoding.UTF8.GetString(bytes, start, end - start - (cr ? 1 : 0)), cr, terminated);
            start = end + 1;
        }
    }
}
