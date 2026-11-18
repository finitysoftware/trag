using System.Globalization;

namespace Trag.Domain.Diff;

// What Git keeps in place of a Git LFS file, by the Git LFS pointer specification: the version line, a line for each
// extension that changed the content, the stored content's SHA-256 and its size in bytes. Anything else is a file of its
// own. Extended, the stored content is what the extensions made of the file.
public sealed record LfsPointer(string Oid, long Size, bool Extended = false)
{
    public const string Version = "version https://git-lfs.github.com/spec/v1";

    public static LfsPointer? Parse(IReadOnlyList<string> lines) => lines is [Version, .., var oid, var size]
        && lines.Skip(1).SkipLast(2).All(line => line.StartsWith("ext-", StringComparison.Ordinal))
        && oid.StartsWith("oid sha256:", StringComparison.Ordinal) && oid.Length == 75 && oid[11..].All(char.IsAsciiHexDigitLower)
        && size.StartsWith("size ", StringComparison.Ordinal) && long.TryParse(size.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var bytes)
            ? new(oid[11..], bytes, lines.Count > 3) : null;

    // The pointers a diff of one compares: the old one from its removed and unchanged lines, the new one from its added
    // and unchanged lines, each without the mark Git puts before it. A side without lines is a file added or deleted;
    // null when the lines are not a pointer's.
    public static (LfsPointer? Before, LfsPointer? After)? Sides(IReadOnlyList<PatchLine> lines)
    {
        string[] before = [.. lines.Where(line => !line.IsHeader && !line.IsAddition).Select(line => line.Text[1..])];
        string[] after = [.. lines.Where(line => !line.IsHeader && !line.IsDeletion).Select(line => line.Text[1..])];
        var (old, now) = (Parse(before), Parse(after));
        return (before.Length == 0 || old is not null) && (after.Length == 0 || now is not null) && (old ?? now) is not null ? (old, now) : null;
    }
}
