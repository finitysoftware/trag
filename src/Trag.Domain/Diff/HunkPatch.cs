using System.Text;
using Trag.Domain.Paths;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Domain.Diff;

// The patch of chosen lines alone, for git apply: from the first chosen line to the last, with up to three lines of
// context on each side; a hunk is all of its lines. A change left out is as the side the patch is applied to has it: a
// removed line is context when staging and an added one when applying in reverse, and the other is left out. Numbered
// on that side: the index's old side when staging, the new side when applying in reverse, to the index to unstage or to
// the working tree to discard. The other side starts where that one does, since no other hunk goes with it. A line
// keeps the CR Git ends it with, and a last line without a newline is marked the way Git marks it, on the sides it is
// still the last line of.
public static class HunkPatch
{
    private const int Context = 3;

    // A hunk is taken alone only from a file changed on the shown side, not added, deleted, renamed or in conflict, and
    // only by a name Git writes into a patch as it is (no quote, backslash or control character to escape).
    public static bool Fits(FileStatus file, bool staged) =>
        !file.IsConflict && (staged ? file.Index : file.Worktree) == 'M' && file.Path.IsDisplayable
        && !file.Path.Display.Any(character => char.IsControl(character) || character is '"' or '\\');

    public static string For(IReadOnlyList<PatchLine> lines, IReadOnlyList<int> chosen, GitPath path, PatchAction action)
    {
        var reverse = action != PatchAction.Stage;
        var picked = chosen.ToHashSet();
        var (first, last) = (chosen.Min(), chosen.Max());
        // A line as the patch has it, or null for a change the side it is applied to never had.
        PatchLine? Patched(int index)
        {
            var line = lines[index];
            if (line.Kind == ' ' || picked.Contains(index)) return line;
            return line.Kind == (reverse ? '+' : '-') ? line with { Kind = ' ', Text = " " + line.Text[1..] } : null;
        }
        var before = new List<PatchLine>();
        for (var index = first - 1; index >= 0 && before.Count < Context && IsLine(lines[index]); index--)
            if (Patched(index) is { } line) before.Insert(0, line);
        var after = new List<PatchLine>();
        for (var index = last + 1; index < lines.Count && after.Count < Context && IsLine(lines[index]); index++)
            if (Patched(index) is { } line) after.Add(line);
        var hunk = Ended([.. before.Concat(Enumerable.Range(first, last - first + 1).Select(Patched).OfType<PatchLine>()).Concat(after)]);
        var (oldCount, newCount) = (hunk.Count(line => line.Kind != '+'), hunk.Count(line => line.Kind != '-'));
        var start = hunk.Select(line => reverse ? line.NewNumber : line.OldNumber).FirstOrDefault(number => number is not null) ?? 0;
        var (count, otherCount) = reverse ? (newCount, oldCount) : (oldCount, newCount);
        // An empty side starts on the line before, as Git numbers it.
        var other = otherCount == 0 ? start - 1 : count == 0 ? start + 1 : start;
        var (oldStart, newStart) = reverse ? (other, start) : (start, other);

        var patch = new StringBuilder();
        patch.Append("--- a/").Append(path.Display).Append('\n').Append("+++ b/").Append(path.Display).Append('\n');
        patch.Append(FormattableString.Invariant($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@\n"));
        foreach (var line in hunk)
        {
            patch.Append(line.Text);
            if (line.HasCarriageReturn) patch.Append('\r');
            patch.Append('\n');
            if (line.NoNewline) patch.Append("\\ No newline at end of file\n");
        }
        return patch.ToString();
    }

    private static bool IsLine(PatchLine line) => line.Kind is ' ' or '+' or '-';

    // Git marks a side's last line when it has no newline. A patch of chosen lines can put a line after it on that side,
    // and there it ends with a newline: a changed line then loses the mark, and an unchanged one that is the old side's
    // last line only is written as removed with the mark and added again without it.
    private static PatchLine[] Ended(PatchLine[] hunk)
    {
        var ended = new List<PatchLine>();
        for (var index = 0; index < hunk.Length; index++)
        {
            var line = hunk[index];
            var later = hunk.Skip(index + 1).ToArray();
            var (oldFollows, newFollows) = (later.Any(next => next.Kind != '+'), later.Any(next => next.Kind != '-'));
            if (line is { NoNewline: true, Kind: ' ' } && newFollows)
            {
                ended.Add(line with { Kind = '-', Text = "-" + line.Text[1..] });
                ended.Add(line with { Kind = '+', Text = "+" + line.Text[1..], NoNewline = false });
            }
            else ended.Add(line with { NoNewline = line.NoNewline && !(line.Kind == '-' ? oldFollows : newFollows) });
        }
        return [.. ended];
    }
}
