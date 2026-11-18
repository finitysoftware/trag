using Trag.Domain.Diff;
using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// The discard question for one hunk of the working tree, or for chosen lines of it, in the dialog's words: the file, what
// the working file loses and gets back, and that there is no undo. It keeps the shown lines the patch is built from.
public sealed class HunkDiscardQuestion(FileStatus file, IReadOnlyList<PatchLine> lines, IReadOnlyList<int> chosen)
{
    public FileStatus File { get; } = file;
    public IReadOnlyList<PatchLine> Lines { get; } = lines;
    public IReadOnlyList<int> Chosen { get; } = chosen;
    public string Title => DiffBlocks.IsWhole(Lines, Chosen) ? "Discard this hunk?" : "Discard the selected lines?";
    public string Subtitle => File.Path.DisplayEscaped;

    public string Description
    {
        get
        {
            var hunk = Chosen.Select(index => Lines[index]).ToArray();
            var parts = new List<string>();
            if (hunk.Count(line => line.Kind == '+') is var added and > 0) parts.Add("loses " + Count(added, "added"));
            if (hunk.Count(line => line.Kind == '-') is var removed and > 0) parts.Add("gets " + Count(removed, "removed") + " back");
            return "The working file " + string.Join(" and ", parts) + "; the rest of it and what is staged stay as they are. There is no undo.";
        }
    }

    private static string Count(int count, string kind) => Counted.Of(count, kind + " line");
}
