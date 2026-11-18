using System.Globalization;
using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Presentation;

// The presentation of a diff in four passes over its display lines: the lines themselves, the split alignment, the
// syntax colors, and the totals for the header and the gutter. Highlighting is bounded and falls back to plain text
// for unsupported languages and large inputs. When and on which thread it runs is the caller's.
public static class DiffPresentationBuilder
{
    public static DiffPresentation Build(FileDiff diff, string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var lines = DisplayLines(diff.Lines, ct);
        var (pairs, map) = AlignRows(lines, ct);
        var (before, after, notice) = Highlight(lines, SyntaxScanner.LanguageOf(path), ct);
        var endings = MixesLineEndings(lines);
        var totals = Measure(lines, endings);
        var change = Change(diff);
        return new(lines, pairs, map, before, after, notice)
        {
            ChangeLabel = change.Label, IsNewFile = change.New, IsDeletedFile = change.Deleted,
            AddedText = FileDiffText.Added(totals.Added, diff.Kind == DiffContentKind.Limited),
            RemovedText = FileDiffText.Removed(totals.Removed, diff.Kind == DiffContentKind.Limited),
            NumberDigits = totals.LargestNumber.ToString(CultureInfo.InvariantCulture).Length,
            MaxColumns = totals.MaxColumns, ShowsLineEndings = endings
        };
    }

    // A last line without a newline has no ending at all, so it counts as neither CRLF nor LF.
    private static bool MixesLineEndings(IReadOnlyList<PatchLine> lines) =>
        lines.Any(line => !line.IsHeader && line.HasCarriageReturn) && lines.Any(line => !line.IsHeader && !line.HasCarriageReturn && !line.NoNewline);

    // Raw hunk headers are not shown; a gap between two hunks of one file is one marker row. A file section starts with
    // its first hunk, so a second section, as a copy's row has when its source changed too, follows the first without a marker.
    private static IReadOnlyList<PatchLine> DisplayLines(IReadOnlyList<PatchLine> source, CancellationToken ct)
    {
        if (!source.Any(line => line.IsHeader)) return source;
        var result = new List<PatchLine>(source.Count);
        var sectionStarts = true;
        foreach (var line in source)
        {
            ct.ThrowIfCancellationRequested();
            if (!line.IsHeader) result.Add(line);
            else if (line.Text.StartsWith("diff ", StringComparison.Ordinal)) sectionStarts = true;
            else if (line.Text.StartsWith("@@", StringComparison.Ordinal))
            {
                if (!sectionStarts) result.Add(new('h', FileDiffText.Omitted));
                sectionStarts = false;
            }
        }
        return result;
    }

    // The split rows: a context or marker line is one row, and a change block is aligned deletion beside addition.
    private static (DiffPair[] Pairs, int[] Map) AlignRows(IReadOnlyList<PatchLine> lines, CancellationToken ct)
    {
        var pairs = new List<DiffPair>(lines.Count);
        var map = new int[lines.Count];
        for (var i = 0; i < lines.Count;)
        {
            ct.ThrowIfCancellationRequested();
            if (lines[i].IsChange)
            {
                i = AlignChanges(lines, i, pairs, map);
                continue;
            }
            map[i] = pairs.Count;
            pairs.Add(ContextPair(lines[i], i));
            i++;
        }
        return (pairs.ToArray(), map);
    }

    // A context line of new content alone sits on the right, of old content alone on the left.
    private static DiffPair ContextPair(PatchLine line, int index)
    {
        if (line.Kind != ' ') return new(index, index);
        if (line.OldNumber is null && line.NewNumber is not null) return new(-1, index);
        if (line.NewNumber is null && line.OldNumber is not null) return new(index, -1);
        return new(index, index);
    }

    // A change block as Git prints it: its deletions, then its additions. The k-th deletion sits beside the k-th
    // addition, and the longer side continues with the other side blank.
    private static int AlignChanges(IReadOnlyList<PatchLine> lines, int start, List<DiffPair> pairs, int[] map)
    {
        var additions = start;
        while (additions < lines.Count && lines[additions].IsDeletion) additions++;
        var end = additions;
        while (end < lines.Count && lines[end].IsAddition) end++;
        var deleted = additions - start;
        var added = end - additions;
        for (var k = 0; k < Math.Max(deleted, added); k++)
        {
            var left = k < deleted ? start + k : -1;
            var right = k < added ? additions + k : -1;
            if (left >= 0) map[left] = pairs.Count;
            if (right >= 0) map[right] = pairs.Count;
            pairs.Add(new(left, right));
        }
        return end;
    }

    // Each side is scanned on its own, so a comment that changes on one side does not color the other; the budget
    // counts both sides of every line and, once reached, leaves the remaining rows plain.
    private static (SyntaxToken[][] Before, SyntaxToken[][] After, string Notice) Highlight(IReadOnlyList<PatchLine> lines, SyntaxLanguage language, CancellationToken ct)
    {
        var before = new SyntaxToken[lines.Count][];
        var after = new SyntaxToken[lines.Count][];
        Array.Fill(before, []);
        Array.Fill(after, []);
        if (language == SyntaxLanguage.Plain) return (before, after, FileDiffText.PlainText);
        var oldSide = new SyntaxScanner(language);
        var newSide = new SyntaxScanner(language);
        var budget = new SyntaxBudget();
        for (var i = 0; i < lines.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var line = lines[i];
            if (line.IsHeader)
            {
                oldSide.Reset();
                newSide.Reset();
                continue;
            }
            if (line.Text.Length == 0) continue;
            if (!budget.Covers(line.Text.Length * 2)) return (before, after, FileDiffText.SyntaxLimitReached);
            if (!line.IsAddition) before[i] = oldSide.Scan(line.Text);
            if (!line.IsDeletion) after[i] = newSide.Scan(line.Text);
            budget.Spend(line.Text.Length * 2, before[i].Length + after[i].Length);
        }
        return (before, after, FileDiffText.SyntaxColors);
    }

    // One pass for the numbers the header and the gutter need: the rows added and removed, the largest line number,
    // and the widest row as it is shown, with tabs and end-of-line markers.
    private static (int Added, int Removed, int LargestNumber, int MaxColumns) Measure(IReadOnlyList<PatchLine> lines, bool endings)
    {
        var added = 0;
        var removed = 0;
        var largest = 0;
        var columns = 0;
        foreach (var line in lines)
        {
            if (line.IsAddition) added++;
            if (line.IsDeletion) removed++;
            largest = Math.Max(largest, Math.Max(line.OldNumber ?? 0, line.NewNumber ?? 0));
            columns = Math.Max(columns, FileDiffText.ShownColumns(line, endings));
        }
        return (added, removed, largest, columns);
    }

    // One reading of Git's status letter gives both the word the badge shows and the two flags the view styles on. A rename's
    // metadata is read without rename detection, as the deletion of its old path and the addition of its new one: that pair,
    // and only a rename's row brings one, is the rename the status list names.
    private static (string Label, bool New, bool Deleted) Change(FileDiff diff)
    {
        var statuses = diff.Metadata.Select(file => file.Status.FirstOrDefault()).Distinct().ToArray();
        if (diff.Metadata.Count == 2 && statuses.Order().SequenceEqual(['A', 'D'])) return (FileDiffText.ChangeLabel('R'), false, false);
        if (statuses.Length != 1) return (diff.Metadata.Count != 0 || diff.Lines.Count != 0 ? FileDiffText.Modified : "", false, false);
        return (FileDiffText.ChangeLabel(statuses[0]), statuses[0] is 'A' or '?', statuses[0] == 'D');
    }
}
