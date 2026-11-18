using System.Globalization;
using System.Text;
using Trag.Domain.Diff;
using Trag.Domain.Paths;
using Trag.Domain.Writes;

namespace Trag.Domain.UnitTests.Diff;

// Any choice of changed lines of a one-hunk diff gives a patch that changes exactly those lines. Each seed makes two small
// files and a diff of them in the shape Git gives with --unified=50000 and Trag reads it: one hunk of every line, a run's
// removed lines before its added ones, lines ending with LF, CRLF or, as the last line, nothing. The patch is applied by a
// reference that is stricter than git apply: it puts the hunk exactly where the header says instead of looking for it
// nearby.
public sealed class HunkPatchPropertyTests
{
    private const int Seeds = 2000;
    private static readonly GitPath Sample = new("docs/sample č.txt"u8.ToArray());

    // Short lines that repeat, so the two files share some: blank, a space, a letter outside ASCII, a CR inside a line,
    // and a CR at the end of one, which with LF after it is a CRLF line, and ended with CRLF keeps its CR as a last line
    // without a newline.
    private static readonly string[] Contents = ["a", "b", "", " ", "č", "a\rb", "a\r"];

    // Staged, the patch is applied to the old side; unstaged or discarded, in reverse to the new side.
    [Theory]
    [InlineData(PatchAction.Stage)]
    [InlineData(PatchAction.Unstage)]
    [InlineData(PatchAction.Discard)]
    public void WhenAnyChoiceOfChangedLinesIsPatched_ShouldChangeExactlyTheChosenLines(PatchAction action) =>
        AssertEverySeed(action, (lines, chosen, patch) =>
            Applied(patch, Side(lines, [], action), action != PatchAction.Stage, Side(lines, chosen, action)));

    // Applied the other way to the side with the chosen lines changed, the same patch gives the side back.
    [Theory]
    [InlineData(PatchAction.Stage)]
    [InlineData(PatchAction.Unstage)]
    [InlineData(PatchAction.Discard)]
    public void WhenThePatchIsAppliedTheOtherWay_ShouldGiveTheOriginalBack(PatchAction action) =>
        AssertEverySeed(action, (lines, chosen, patch) =>
            Applied(patch, Side(lines, chosen, action), action == PatchAction.Stage, Side(lines, [], action)));

    // Every seed is checked, and the seeds that fail are named in one failure, the first few with their choice and patch.
    private static void AssertEverySeed(PatchAction action, Func<PatchLine[], int[], string, string?> wrong)
    {
        var failures = new List<(int Seed, string Detail)>();
        for (var seed = 0; seed < Seeds; seed++)
        {
            var random = new Random(seed);
            var lines = Diff(random);
            var chosen = Choose(random, lines);
            string? patch = null;
            try
            {
                patch = HunkPatch.For(lines, chosen, Sample, action);
                if (wrong(lines, chosen, patch) is { } why) failures.Add((seed, $"seed {seed}: chosen [{string.Join(", ", chosen)}], {why}, patch {Shown(patch)}"));
            }
            catch (Exception exception)
            {
                failures.Add((seed, $"seed {seed}: chosen [{string.Join(", ", chosen)}], {exception.GetType().Name}: {exception.Message}, patch {Shown(patch)}"));
            }
        }
        if (failures.Count != 0)
            Assert.Fail($"{failures.Count} of {Seeds} seeds failed: {string.Join(", ", failures.Select(failure => failure.Seed))}\n"
                + string.Join("\n", failures.Take(5).Select(failure => failure.Detail)));
    }

    // Null when the patch applied to the text gives the expected one, or what went wrong.
    private static string? Applied(string patch, string text, bool reverse, string expected)
    {
        var (result, refusal) = Apply(patch, text, reverse);
        return refusal ?? (result == expected ? null : $"gave {Shown(result)} instead of {Shown(expected)}");
    }

    // Up to twelve lines ending with LF, CRLF or either, and the same lines with some removed, replaced or followed by new
    // ones, a new first line or one ending changed; either file may lose its last newline or be empty, but they differ.
    // Their diff follows a longest common subsequence of whole lines.
    private static PatchLine[] Diff(Random random)
    {
        var ending = random.Next(3) switch { 0 => "\n", 1 => "\r\n", _ => null };
        string Line(string? end) => Contents[random.Next(Contents.Length)] + (end ?? (random.Next(2) == 0 ? "\n" : "\r\n"));
        var old = Enumerable.Range(0, random.Next(13)).Select(_ => Line(ending)).ToList();
        var changed = new List<string>();
        foreach (var line in old)
        {
            var roll = random.NextDouble();
            if (roll < 0.55) changed.Add(line);
            else if (roll < 0.7) continue;
            else if (roll < 0.85) changed.Add(Line(EndingOf(line)));
            else changed.AddRange([line, .. Enumerable.Range(0, random.Next(1, 4)).Select(_ => Line(EndingOf(line)))]);
        }
        if (random.NextDouble() < 0.15) changed.Insert(0, Line(ending));
        if (random.NextDouble() < 0.1 && changed.Count != 0)
        {
            var index = random.Next(changed.Count);
            var end = EndingOf(changed[index]);
            changed[index] = changed[index][..^end.Length] + (end == "\n" ? "\r\n" : "\n");
        }
        foreach (var file in new[] { old, changed })
            if (file.Count != 0 && random.NextDouble() < 0.3)
                file[^1] = file[^1][..^EndingOf(file[^1]).Length] is { Length: > 0 } text ? text : "a";
        if (changed.SequenceEqual(old)) changed.Insert(0, Line(ending));
        return Diff(old, changed);
    }

    private static string EndingOf(string line) => line.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : line.EndsWith('\n') ? "\n" : "";

    // A header, then every line of both files: a run of changes as its removed lines and then its added ones.
    private static PatchLine[] Diff(List<string> old, List<string> changed)
    {
        var common = new int[old.Count + 1, changed.Count + 1];
        for (var i = old.Count - 1; i >= 0; i--)
            for (var j = changed.Count - 1; j >= 0; j--)
                common[i, j] = old[i] == changed[j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
        var lines = new List<PatchLine> { new('h', FormattableString.Invariant($"@@ -{(old.Count == 0 ? 0 : 1)},{old.Count} +{(changed.Count == 0 ? 0 : 1)},{changed.Count} @@")) };
        var (removed, added) = (new List<PatchLine>(), new List<PatchLine>());
        void Flush()
        {
            lines.AddRange(removed);
            lines.AddRange(added);
            removed.Clear();
            added.Clear();
        }
        for (int i = 0, j = 0; i < old.Count || j < changed.Count;)
        {
            if (i < old.Count && j < changed.Count && old[i] == changed[j])
            {
                Flush();
                lines.Add(Read(' ', old[i], i + 1, j + 1));
                (i, j) = (i + 1, j + 1);
            }
            else if (j < changed.Count && (i == old.Count || common[i, j + 1] >= common[i + 1, j]))
            {
                added.Add(Read('+', changed[j], null, j + 1));
                j++;
            }
            else
            {
                removed.Add(Read('-', old[i], i + 1, null));
                i++;
            }
        }
        Flush();
        return [.. lines];
    }

    // A line as Trag reads Git's output: CRLF is a flag, and a last line without a newline keeps a CR it ends with.
    private static PatchLine Read(char kind, string line, int? oldNumber, int? newNumber) => EndingOf(line) switch
    {
        "\r\n" => new(kind, kind + line[..^2], oldNumber, newNumber) { HasCarriageReturn = true },
        "\n" => new(kind, kind + line[..^1], oldNumber, newNumber),
        _ => new(kind, kind + line, oldNumber, newNumber) { NoNewline = true }
    };

    // As the diff view chooses: changed lines in order, never none, and in some cases the run of one, as a hunk is taken.
    private static int[] Choose(Random random, PatchLine[] lines)
    {
        var changes = Enumerable.Range(0, lines.Length).Where(index => lines[index].IsChange).ToArray();
        if (random.NextDouble() < 0.3) return [.. DiffBlocks.At(lines, changes[random.Next(changes.Length)])!.Value.Lines];
        var share = random.NextDouble();
        var chosen = changes.Where(_ => random.NextDouble() < share).ToArray();
        return chosen.Length != 0 ? chosen : [changes[random.Next(changes.Length)]];
    }

    // The side the patch is applied to, the old one when staging and the new one in reverse, with the chosen lines changed
    // on it: a chosen line the patch takes away is gone and one it brings is there. A line without a newline that another
    // follows ends with one.
    private static string Side(PatchLine[] lines, IReadOnlyCollection<int> chosen, PatchAction action)
    {
        var brought = action == PatchAction.Stage ? '+' : '-';
        var kept = lines.Where((line, index) => line.Kind == ' ' || line.IsChange && chosen.Contains(index) == (line.Kind == brought)).ToArray();
        var text = new StringBuilder();
        for (var index = 0; index < kept.Length; index++)
            text.Append(kept[index].Text[1..]).Append(kept[index].HasCarriageReturn ? "\r\n" : kept[index].NoNewline && index == kept.Length - 1 ? "" : "\n");
        return text.ToString();
    }

    // git apply of a patch of one hunk, stricter: the hunk goes exactly where its header puts it on the side it is applied
    // to. As in Git, --reverse swaps the sides, \ No newline takes the newline off the line before it, a hunk that starts
    // on line 0 or 1 matches at the start of the file and one without context after its last change at the end, and the
    // numbers of lines must be right. The text it gives, or why it refuses.
    private static (string? Text, string? Refusal) Apply(string patch, string target, bool reverse)
    {
        var rows = patch.Split('\n');
        if (rows.Length < 4 || rows[0] != "--- a/" + Sample.Display || rows[1] != "+++ b/" + Sample.Display || rows[^1].Length != 0)
            return (null, "no patch of the file");
        var header = rows[2].Split(' ');
        if (header.Length != 4 || header[0] != "@@" || header[3] != "@@" || !header[1].StartsWith('-') || !header[2].StartsWith('+'))
            return (null, "no hunk header");
        var (oldStart, oldCount) = StartAndCount(header[1]);
        var (newStart, newCount) = StartAndCount(header[2]);
        if (reverse) (oldStart, oldCount, newStart, newCount) = (newStart, newCount, oldStart, oldCount);
        var hunk = new List<(char Kind, string Line)>();
        foreach (var row in rows[3..^1])
        {
            if (row == "\\ No newline at end of file" && hunk.Count != 0 && hunk[^1].Line.EndsWith('\n')) hunk[^1] = (hunk[^1].Kind, hunk[^1].Line[..^1]);
            else if (row.Length != 0 && row[0] is ' ' or '+' or '-') hunk.Add((reverse ? row[0] switch { '+' => '-', '-' => '+', _ => ' ' } : row[0], row[1..] + "\n"));
            else return (null, "not a line of a hunk: " + Shown(row));
        }
        var before = hunk.Where(line => line.Kind != '+').Select(line => line.Line).ToArray();
        var after = hunk.Where(line => line.Kind != '-').Select(line => line.Line).ToArray();
        if (hunk.Count == 0 || before.Length != oldCount || after.Length != newCount) return (null, "numbers of lines that the hunk does not have");
        var lines = Lines(target);
        var at = oldCount == 0 ? oldStart : oldStart - 1;
        if (oldStart <= 1 && at != 0) return (null, "a hunk from line 0 or 1 that is not at the start");
        if (at < 0 || at + oldCount > lines.Count || !lines.Skip(at).Take(oldCount).SequenceEqual(before)) return (null, "lines that are not where the header puts them");
        if (hunk[^1].Kind != ' ' && at + oldCount != lines.Count) return (null, "a hunk without context after it that is not at the end");
        var result = lines.Take(at).Concat(after).Concat(lines.Skip(at + oldCount)).ToArray();
        if (result.SkipLast(1).Any(line => !line.EndsWith('\n'))) return (null, "a line without a newline before another");
        return (string.Concat(result), null);
    }

    private static (int Start, int Count) StartAndCount(string range)
    {
        var numbers = range[1..].Split(',');
        return (int.Parse(numbers[0], CultureInfo.InvariantCulture), int.Parse(numbers[1], CultureInfo.InvariantCulture));
    }

    // A text's lines, each with its LF; a CR stays in the line, as git apply keeps it.
    private static List<string> Lines(string text)
    {
        var lines = new List<string>();
        for (var start = 0; start < text.Length;)
        {
            var end = text.IndexOf('\n', start) + 1;
            if (end == 0) end = text.Length;
            lines.Add(text[start..end]);
            start = end;
        }
        return lines;
    }

    private static string Shown(string? text) => text is null ? "nothing" : "\"" + text.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
}
