using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// The discard question over Domain's DiscardSet: what goes, in the dialog's words. There is no undo, and it says so.
public sealed class DiscardQuestion(DiscardSet set)
{
    public DiscardSet Set { get; } = set;
    public bool IsEmpty => Set.IsEmpty;
    public GitPath[] Paths => Set.Paths;

    public string Title => Set.Files.Count switch
    {
        0 => "",
        1 => Set.Files[0].IsUntracked ? "Delete untracked file?" : "Discard changes?",
        var count => "Discard changes in " + Counted.Of(count, "file") + "?"
    };

    public string Subtitle
    {
        get
        {
            var names = string.Join(", ", Set.Files.Take(4).Select(file => file.Path.DisplayEscaped));
            if (Set.Files.Count <= 4)
                return names;
            return names + " and " + Counted.Number(Set.Files.Count - 4) + " more";
        }
    }

    public string Description
    {
        get
        {
            var parts = new List<string>();
            if (Set.TrackedCount > 0)
                parts.Add(TrackedText(Set.TrackedCount) + " lost; what is staged stays staged.");
            if (Set.UntrackedCount > 0)
                parts.Add(UntrackedText(Set.UntrackedCount) + " deleted from disk.");
            parts.Add("There is no undo.");
            return string.Join(" ", parts);
        }
    }

    private static string TrackedText(int count) => count == 1 ? "The working tree change to this file is"
        : "The working tree changes to " + Counted.Of(count, "file") + " are";
    private static string UntrackedText(int count) => Counted.Of(count, "untracked file") + (count == 1 ? " is" : " are");
}
