using System.Text;
using Trag.Domain.Merge;

namespace Trag.TestSupport;

// A generated file, and what it reads as once every block is taken as the side chosen for it.
public sealed record ConflictedFile(string Text, IReadOnlyList<string> Outside, IReadOnlyList<(string Mine, string Theirs)> Sides)
{
    public string Resolved(IReadOnlyList<ConflictChoice> choices) =>
        Outside[0] + string.Concat(Sides.Select((side, i) => choices[i] switch
        {
            ConflictChoice.Mine => side.Mine,
            ConflictChoice.Theirs => side.Theirs,
            _ => side.Mine + side.Theirs,
        } + Outside[i + 1]));
}

// Writes a file the way Git 2.55 was seen to write a conflicted one: one conflict style for the whole file, conflicts at
// least four lines apart (Git joins closer ones), the markers of a conflict end with one line ending, every line of a
// side ends (Git adds the ending a side's last line lacks), a byte order mark starts the file, or every side of the
// conflict the file starts with, and only the file's last line may lack its ending. In a file of mixed endings a
// conflict's ending is drawn at random, where Git takes the ending of the line before the conflict. No side holds a line
// that is exactly a marker, since the text alone cannot tell where such a side ends. Outside the conflicts a separator
// or a base marker is the file's own text, and so is a closing marker before the first conflict.
public sealed class ConflictedFileShape(Random random, string endings)
{
    public const string Bom = "\uFEFF";

    private static readonly string[] Labels = ["", "HEAD", "ours", "theirs", "origin/main", "feature/a b", "Stashed changes", "parent of 1a2b3c4 (Fix)"];
    private static readonly string[] Pieces = ["a", "Z", "7", " ", "\t", "č", "ž", "é", "中", "😀"];

    // Lines that are text, not markers: a run of marker characters one too long or too short, a label with no space
    // before it, a separator with more after it, a space or a byte order mark before a marker.
    private static readonly string[] NearMarkers =
    [
        "<<<<<<<<", "========", ">>>>>>>>", "||||||||", "<<<<<<", "======", "<<<<<<<HEAD", "|||||||base", ">>>>>>>x",
        "======= ", "=======x", " <<<<<<< HEAD", " =======", Bom + "<<<<<<< HEAD", Bom + "=======",
    ];

    public string WithoutConflicts() =>
        (random.Next(2) == 0 ? Bom : "") + Lines(random.Next(12), () => OutsideLine(beforeFirstBlock: true), last: true);

    public ConflictedFile Conflicted()
    {
        var bom = random.Next(2) == 0;
        var before = Lines(random.Next(4), () => OutsideLine(beforeFirstBlock: true), last: false);
        // The mark belongs to the first line, so a file that starts with a conflict has it at the start of every side.
        var mark = bom && before.Length == 0 ? Bom : "";
        var outside = new List<string> { (bom && before.Length > 0 ? Bom : "") + before };
        var sides = new List<(string Mine, string Theirs)>();
        var text = new StringBuilder(outside[0]);
        var count = random.Next(1, 6);
        var diff3 = random.Next(2) == 0;
        for (var block = 0; block < count; block++)
        {
            var least = mark.Length > 0 ? 1 : 0;
            var mine = mark + Lines(random.Next(least, 4), TextLine, last: false);
            var theirs = mark + Lines(random.Next(mine.Length > 0 ? least : 1, 4), TextLine, last: false);
            var end = EndOfLine();
            text.Append(Marker('<') + end).Append(mine);
            if (diff3) text.Append(BaseMarker() + end).Append(mark + Lines(random.Next(least, 3), TextLine, last: false));
            text.Append("=======" + end).Append(theirs).Append(Marker('>') + end);
            var last = block == count - 1;
            var after = Lines(random.Next(last ? 0 : 4, 7), () => OutsideLine(beforeFirstBlock: false), last);
            text.Append(after);
            sides.Add((mine, theirs));
            outside.Add(after);
            mark = "";
        }
        return new(text.ToString(), outside, sides);
    }

    private string Lines(int count, Func<string> line, bool last) =>
        string.Concat(Enumerable.Range(0, count).Select(i => line() + (last && i == count - 1 && random.Next(2) == 0 ? "" : EndOfLine())));

    private string OutsideLine(bool beforeFirstBlock) => random.Next(6) switch
    {
        0 => "=======",
        1 => Marker('|'),
        2 when beforeFirstBlock => Marker('>'),
        _ => TextLine(),
    };

    private string TextLine() => random.Next(5) == 0
        ? NearMarkers[random.Next(NearMarkers.Length)]
        : string.Concat(Enumerable.Range(0, random.Next(9)).Select(_ => Pieces[random.Next(Pieces.Length)]));

    private string Marker(char mark)
    {
        var label = Labels[random.Next(Labels.Length)];
        return new string(mark, 7) + (label.Length > 0 ? " " + label : "");
    }

    // Git always names the base: its commit, "base", "parent of …" or "Stash base".
    private string BaseMarker() => "||||||| " + Labels[random.Next(1, Labels.Length)];

    private string EndOfLine() => endings switch
    {
        "LF" => "\n",
        "CRLF" => "\r\n",
        _ => random.Next(2) == 0 ? "\n" : "\r\n",
    };
}
