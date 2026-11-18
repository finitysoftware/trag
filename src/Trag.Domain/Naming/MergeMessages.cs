using System.Globalization;
using Trag.Domain.Branches;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.Naming;

// The message a merge commit starts with: Git's own wording, with the target branch always named. Git adds a commented
// "# Conflicts:" list to the message of a merge that stopped; the merge view offers the message without those lines,
// and reads the incoming branch's name back out of Git's first line.
public static class MergeMessages
{
    private const string Local = "Merge branch '";
    private const string Remote = "Merge remote-tracking branch '";
    private const string Tag = "Merge tag '";

    public static string Default(BranchInfo? from, BranchInfo? into) => from is null || into is null ? ""
        : (from.IsRemote ? Remote : Local) + from.Name + "' into " + into.Name;

    // A pull merges the tip its fetch read, by id; the message names the remote-tracking branch that tip is on.
    public static string Pull(string trackingRef, string into) => Remote + RefNames.RemoteTrackingName(trackingRef) + "' into " + into;

    // A merge message follows the branches while it is still the one Trag wrote for the previous ones; one the user
    // wrote stays.
    public static string Following(string message, string previousDefault, string nextDefault) => message == previousDefault ? nextDefault : message;

    // Git's list ends the message: "<c> Conflicts:" and a "<c><tab><path>" line for each file, <c> being core.commentChar,
    // after its scissors when commit.cleanup is scissors. Every other line is the message's own, one starting with '#' too.
    // What is left may be Git's text for a fold.
    public static string Clean(string message)
    {
        var lines = WithoutConflictList(message.Replace("\r\n", "\n").TrimEnd().Split('\n'));
        return Folded(lines) ?? string.Join('\n', lines).TrimEnd();
    }

    private static string[] WithoutConflictList(string[] lines)
    {
        var heading = Array.FindLastIndex(lines, line => line.EndsWith(Heading, StringComparison.Ordinal));
        if (heading < 0) return lines;
        var comment = lines[heading][..^Heading.Length];
        var paths = lines[(heading + 1)..];
        if (paths.Length == 0 || paths.Any(line => !line.StartsWith(comment + "\t", StringComparison.Ordinal))) return lines;
        return lines[..(heading >= 4 && lines[heading - 4] == comment + Scissors ? heading - 4 : heading)];
    }

    private const string Heading = " Conflicts:";
    private const string Scissors = " ------------------------ >8 ------------------------";

    // At a stop on a fold the message is Git's text for its editor: "<c> This is a combination of <n> commits.", <c> being
    // the comment character, then a heading for each commit, numbered in order: "<c> This is the 1st commit message:" or
    // "<c> This is the commit message #<n>:" over a message Git keeps as it is, "<c> The commit message #<n> will be
    // skipped:" or, for a fixup -C, "<c> The 1st commit message will be skipped:" over one it comments out. A kept message
    // that starts with an autosquash subject ("amend!", "squash!", "fixup!") has that paragraph, up to a line of only
    // spaces, commented out too. The fold's message is the messages kept, the blank lines between them run together. A kept
    // message that is itself Git's text for a fold, as earlier Trag builds committed it, is read the same way, and so is one
    // inside that: only Git's exact first line in a kept message opens it, its headings are numbered on their own, and the
    // last of them, the <n> of its first line, closes it. The fold's next heading that no later line repeats closes every
    // such text, so one that lacks its last headings ends there; one that a later line repeats goes to the open text whose
    // next heading it is, or, when it would be that text's last and the text under it waits for the same number while the
    // heading of the number after it comes first, to the text under it, the one on top lacking its last headings. A later
    // kept message that is itself such a text has headings of its own, numbered from 1, and they repeat none of the fold's.
    private static string? Folded(string[] lines)
    {
        if (lines.Length < 2) return null;
        var at = lines[0].IndexOf(Combination, StringComparison.Ordinal);
        if (at <= 0) return null;
        var comment = lines[0][..at];
        if (Keeps(lines[1], comment, 1) is null) return null;
        var kept = new List<string>();
        var (keeping, opening, marked) = (false, false, false);
        var ordinal = 1;
        var levels = new Stack<(int Next, int Count)>();
        var known = new Dictionary<int, bool>();
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            var heading = Keeps(line, comment, ordinal);
            var last = heading is not null && !Repeats(lines, index + 1, comment, ordinal, heading.Value, known);
            if (!last && levels.TryPeek(out var level) && Keeps(line, comment, level.Next) is { } innerHeading)
            {
                if (LacksItsLastHeadings(levels, lines, index, comment)) levels.Pop();
                (keeping, opening) = (innerHeading, true);
                Advance(levels);
                continue;
            }
            if (heading is { } keeps)
            {
                (keeping, opening) = (keeps, true);
                if (last) levels.Clear();
                ordinal++;
                continue;
            }
            if (keeping && Opens(lines, index, comment) is { } count) { levels.Push((1, count)); continue; }
            if (opening && line.Length != 0) (opening, marked) = (false, Marks.Any(mark => line.StartsWith(comment + mark, StringComparison.Ordinal)));
            if (marked) { marked = !string.IsNullOrWhiteSpace(line); continue; }
            if (keeping && (line.Length != 0 || (kept.Count != 0 && kept[^1].Length != 0))) kept.Add(line);
        }
        return string.Join('\n', kept).TrimEnd('\n');
    }

    // Whether a later line is the fold's heading of this number. The lookahead reads the fold's later headings in order, and
    // a text for a fold that opens in a message it reads as kept with that text's own headings; both say whether Git keeps
    // the message under them. A heading of the number both the open text and the fold wait for is the fold's when the
    // lookahead, asked from the line after it, does not find the fold's heading of that number again: the fold then waits
    // for the next number and the text still waits for its heading. Any other heading of the open text's next number moves
    // the text on. A heading of a number from 2 up that the lookahead has passed, in a message it reads as kept, shows that
    // it took a text's heading for the fold's, and then this heading is taken to repeat; a 1st heading there that opens no
    // text is a quote the message keeps. The answer depends only on the line the lookahead starts at, as the heading before it gives
    // the number and whether Git keeps the message under it, so known keeps each answer for one Clean and no start is
    // read twice.
    private static bool Repeats(string[] lines, int from, string comment, int ordinal, bool keeping, Dictionary<int, bool> known)
    {
        if (known.TryGetValue(from, out var repeats)) return repeats;
        var next = ordinal + 1;
        var levels = new Stack<(int Next, int Count)>();
        for (var index = from; index < lines.Length && !repeats; index++)
        {
            var line = lines[index];
            if (levels.TryPeek(out var level) && Keeps(line, comment, level.Next) is { } inner)
            {
                keeping = inner;
                if (level.Next == next && !Repeats(lines, index + 1, comment, next, inner, known)) next++;
                else Advance(levels);
            }
            else if (Keeps(line, comment, ordinal) is not null) repeats = true;
            else if (Keeps(line, comment, next) is { } heading) (keeping, next) = (heading, next + 1);
            else if (keeping && Opens(lines, index, comment) is { } count) levels.Push((1, count));
            else repeats = keeping && Enumerable.Range(2, next - 2).Any(passed => Keeps(line, comment, passed) is not null);
        }
        return known[from] = repeats;
    }

    // Whether the open text on top lacks its last headings: the heading at this line would be its last, the text under it
    // waits for the same number, and further down the heading of the number after it comes before that number does again.
    private static bool LacksItsLastHeadings(Stack<(int Next, int Count)> levels, string[] lines, int index, string comment)
    {
        var open = levels.ToArray();
        var next = open[0].Next;
        return open.Length > 1 && open[0].Count == next && open[1].Next == next
            && Array.Find(lines[(index + 1)..], later => Keeps(later, comment, next) is not null || Keeps(later, comment, next + 1) is not null) is { } following
            && Keeps(following, comment, next + 1) is not null;
    }

    // An open text's heading moves it on to its next one, and its last heading closes it.
    private static void Advance(Stack<(int Next, int Count)> levels)
    {
        var (next, count) = levels.Pop();
        if (next < count) levels.Push((next + 1, count));
    }

    private static readonly string[] Marks = [" amend!", " squash!", " fixup!"];

    // Whether a line is Git's heading of the given number over a message it keeps, or over one it skips; null otherwise.
    private static bool? Keeps(string line, string comment, int ordinal) =>
        line == comment + (ordinal == 1 ? " This is the 1st commit message:" : " This is the commit message #" + ordinal + ":") ? true
        : line == comment + (ordinal == 1 ? " The 1st commit message will be skipped:" : " The commit message #" + ordinal + " will be skipped:") ? false
        : null;

    private const string Combination = " This is a combination of ";

    // The <n> of Git's first line of a fold, "<c> This is a combination of <n> commits.", n being 2 or more; null for any
    // other line.
    private static int? CommitCount(string line, string comment)
    {
        var rest = line.StartsWith(comment + Combination, StringComparison.Ordinal) ? line[(comment.Length + Combination.Length)..] : "";
        return rest.EndsWith(Commits, StringComparison.Ordinal)
            && int.TryParse(rest[..^Commits.Length], NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= 2 ? count : null;
    }

    private const string Commits = " commits.";

    // The <n> of a text for a fold that starts at this line: Git writes its first line of a fold, its first heading and an
    // empty line; a message that only quotes the two lines goes on without the empty line. Null for any other line.
    private static int? Opens(string[] lines, int first, string comment) =>
        first + 2 < lines.Length && Keeps(lines[first + 1], comment, 1) is not null && lines[first + 2].Length == 0 ? CommitCount(lines[first], comment) : null;

    // "Merge branch 'x'…", "Merge remote-tracking branch 'origin/x'…" or "Merge tag 'v1'"; any other message, a commit
    // merged by its id included, leaves the commit's short id.
    public static string IncomingName(RepositoryMerge? merge)
    {
        var first = (merge?.Message ?? "").Split('\n', 2)[0];
        foreach (var prefix in new[] { Local, Remote, Tag })
        {
            if (!first.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var end = first.IndexOf('\'', prefix.Length);
            if (end > prefix.Length) return first[prefix.Length..end];
        }
        return merge?.IncomingHead is { } head ? GitObjectId.Short(head) : MergeText.IncomingCommit;
    }
}
