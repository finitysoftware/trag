using System.Text;
using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Infrastructure.Git.Parsing;

// git status --porcelain=v2 -z: the one status format the application reads. Header records and file records come out
// of the same stream and are read in one pass, which is also what keeps a rename source that looks like a header from
// being taken for one: the record after a rename is always its source path, whatever it begins with.
internal static class StatusParser
{
    public static StatusRecords Parse(byte[] bytes)
    {
        string? branch = null, head = null, upstream = null;
        var sawBranch = false;
        var sawHead = false;
        var files = new List<FileStatus>();
        using var records = NulRecordReader.Records(bytes).GetEnumerator();
        while (records.MoveNext())
        {
            var row = records.Current;
            if (row.Length < 3) throw new FormatException("Invalid status record.");
            var kind = (char)row[0];
            if (kind == '#') ReadHeader(row);
            else if (kind is '?' or '!') files.Add(new(new(row[2..]), kind, kind));
            else files.Add(ReadChange(row, kind, records));
        }
        return new(branch, head, upstream, sawBranch && sawHead, files);

        void ReadHeader(byte[] record)
        {
            var span = record.AsSpan();
            if (span.StartsWith("# branch.head "u8))
            {
                var value = Encoding.UTF8.GetString(span[14..]);
                branch = value == "(detached)" ? null : value;
                sawBranch = true;
            }
            else if (span.StartsWith("# branch.oid "u8))
            {
                var value = Encoding.UTF8.GetString(span[13..]);
                head = value == "(initial)" ? null : value;
                sawHead = true;
            }
            else if (span.StartsWith("# branch.upstream "u8)) upstream = Encoding.UTF8.GetString(span[18..]);
        }
    }

    // "1" is an ordinary change, "2" a rename or copy whose source follows as the next record, "u" an unmerged file.
    // The path begins after a fixed number of space separated fields; the fields Trag shows sit at known offsets. The
    // working tree's mode is the last of the modes: after HEAD's and the index's, or after the three stages' of a conflict.
    private static FileStatus ReadChange(byte[] row, char kind, IEnumerator<byte[]> records)
    {
        var (spaces, worktreeMode) = kind switch { '1' => (8, 5), '2' => (9, 5), 'u' => (10, 6), _ => throw new FormatException("Unknown status record.") };
        var pathOffset = 0;
        var noFile = false;
        for (var s = 0; s < spaces; s++)
        {
            var offset = Array.IndexOf(row, (byte)' ', pathOffset);
            if (offset < 0) throw new FormatException("Incomplete status record.");
            if (s == worktreeMode) noFile = HoldsNoFile(row.AsSpan(pathOffset..offset));
            pathOffset = offset + 1;
        }
        GitPath? original = null;
        if (kind == '2')
        {
            if (!records.MoveNext()) throw new FormatException("Missing rename source.");
            original = new(records.Current);
        }
        // The submodule field is "S<c><m><u>": c is 'C' when its commit is not the one the index records, m 'M' for its own
        // modified files and u 'U' for its own untracked ones.
        return new(new(row[pathOffset..]), (char)row[2], (char)row[3], original, kind == 'u', row[5] == 'S', row[5] == 'S' && row[6] == 'C',
            row[5] == 'S' && (row[7] == 'M' || row[8] == 'U'), noFile);
    }

    // A submodule's entry (a commit of another repository) or nothing at all.
    private static bool HoldsNoFile(ReadOnlySpan<byte> mode) => mode.SequenceEqual("160000"u8) || mode.SequenceEqual("000000"u8);
}
