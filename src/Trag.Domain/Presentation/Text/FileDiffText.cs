using Trag.Domain.Diff;

namespace Trag.Domain.Presentation.Text;

// What a file's diff says instead of its content, or about the part of it that is shown: binary, limited, large, new,
// or not readable as text at all. The limits it names are the ones the readers apply.
public static class FileDiffText
{
    public const string BinaryDiff = "Binary file · a text diff is not available.";
    public const string BinaryPreview = "Binary file · a text preview is not available.";
    public static readonly string LimitedDiff = "Limited preview · first " + Counted.Number(DiffLimits.PreviewLines) + " diff rows, long lines clipped.";
    public static readonly string LimitedContent = "Limited preview · first " + Counted.Number(DiffLimits.PreviewLines) + " rows, long lines clipped.";
    public static readonly string LargeDiff = "Large diff · " + Exceeds;
    public static readonly string LargeContent = "Large content · " + Exceeds;
    public const string NoChanges = "No content changes in this source.";
    public const string NewEmptyFile = "New empty file.";
    public const string EmptyFileDeleted = "Empty file deleted.";
    public static string ModeChanged(string from, string to) => "Only the file mode changed · " + from + " → " + to + ".";
    public const string NewFile = "New file · all content is added.";

    // A file too large for a whole diff: first the offer, then the bounded preview; a conflicted file shows the same
    // bounded preview of its working file.
    public static readonly string LargeFileOffer = "Large file · exceeds " + Counted.Megabytes(DiffLimits.ByteLimit) + ". Load a bounded content preview to inspect the beginning.";
    public static readonly string ContentPreview = "Content preview only · " + Bounded + ", long lines clipped. This is not a complete diff.";
    public static readonly string ConflictedWorkingFile = "Conflicted working file · " + Bounded + ". Review conflict markers, edit externally, then stage the resolution.";
    public const string ConflictWithoutWorkingFile = "Conflict with a deleted working file. Stage the deletion to keep it, or restore the desired content before staging.";
    public const string ContentGone = "Content is no longer available. Refresh the status.";

    // Why a file shows no content at all, worded the same for a working-tree diff, a commit's and a conflict.
    public const string Link = "Symbolic link or junction · target contents are not read.";
    public const string Submodule = "Submodule · its recorded commit is unchanged; its own changes show when you open it.";
    public const string InnerRepository = "Another Git repository inside this one · its changes show when you open it on its own.";
    public const string NonUtf8Path = "Path with non-UTF-8 bytes · identity is preserved; a text preview is unavailable.";
    public const string NotDownloaded = "Content not downloaded · this version of the file is not in the local repository; a partial clone leaves it on the remote, and Trag does not fetch it.";

    private static string Exceeds => "exceeds " + Counted.Megabytes(DiffLimits.ByteLimit) + ", " + Counted.Grouped(DiffLimits.LineLimit) + " rows or "
        + Counted.Grouped(DiffLimits.LineLengthLimit) + " characters per line.";
    private static string Bounded => "first " + Counted.Number(DiffLimits.PreviewLines) + " rows / " + Counted.Kilobytes(DiffLimits.PreviewBytes);

    // The diff header: the change's badge, how many lines each side changed, how the rows are colored, and the row that
    // stands for the unchanged lines between two hunks.
    public const string Modified = "Modified";
    public static string ChangeLabel(char status) => status switch
    {
        'A' or '?' => "New File", 'D' => "Deleted File", 'R' => "Renamed", 'C' => "Copied", 'U' => "Conflict", _ => Modified
    };
    public static string Added(int count, bool preview) => LineCount(count, "added", preview);
    public static string Removed(int count, bool preview) => LineCount(count, "removed", preview);
    public const string PlainText = "Plain text";
    public const string SyntaxLimitReached = "Syntax color limit reached · remaining rows in plain text";
    public const string SyntaxColors = "Syntax colors";
    public const string Omitted = "Unchanged lines omitted";

    private static string LineCount(int count, string action, bool preview) => count == 0 ? ""
        : Counted.Of(count, "line") + " " + action + (preview ? " (preview)" : "");

    // What a diff row adds after its text: a changed line that starts with a byte order mark, a line Git ends with CRLF,
    // and a last line without a newline.
    public const string BomMark = "  · UTF-8 BOM";
    public const string CrlfMark = "  · CRLF";
    public const string NoNewlineMark = "  · No newline at end of file";
    // A tab as a row shows it, one column wide plus the columns it adds.
    public const string Tab = "→   ";

    // A line as its row shows it: tabs as arrows, a carriage return as ␍, and the marks after it; lineEndings says the
    // diff marks CRLF endings, because its lines end both ways.
    public static string Shown(PatchLine line, bool lineEndings) =>
        line.Text.Replace("\t", Tab, StringComparison.Ordinal).Replace('\r', '␍') + (StartsWithBom(line) ? BomMark : "")
        + (lineEndings && line.HasCarriageReturn ? CrlfMark : "") + (line.NoNewline ? NoNewlineMark : "");

    // The columns of the shown line, counted without building it.
    public static int ShownColumns(PatchLine line, bool lineEndings) =>
        Columns(line.Text) + line.Text.AsSpan().Count('\t') * (Tab.Length - 1) + (StartsWithBom(line) ? BomMark.Length : 0)
        + (lineEndings && line.HasCarriageReturn ? CrlfMark.Length : 0) + (line.NoNewline ? NoNewlineMark.Length : 0);

    public static int Columns(ReadOnlySpan<char> text)
    {
        var columns = 0;
        foreach (var unit in text) columns += Columns(unit);
        return columns;
    }

    // The whole cells a row gives one UTF-16 unit of a line. The monospace font has no glyphs for East Asian wide and
    // fullwidth characters or for emoji: the fallback fonts draw such a character about 1.7 cells wide, so it takes two,
    // and an emoji past the basic plane about 2.3, so its high surrogate takes two and its low surrogate one.
    public static int Columns(char unit) => unit is >= '\u1100' and <= '\u115F' or >= '\u2E80' and <= '\uA4CF'
        or >= '\uAC00' and <= '\uD7A3' or >= '\uF900' and <= '\uFAFF' or >= '\uFE30' and <= '\uFE6F' or >= '\uFF00' and <= '\uFF60'
        or >= '\uFFE0' and <= '\uFFE6' or >= '\uD83C' and <= '\uD83E' ? 2 : 1;

    // The mark comes right after the + or - of the line.
    private static bool StartsWithBom(PatchLine line) => line.IsChange && line.Text.Length > 1 && line.Text[1] == '﻿';
}
