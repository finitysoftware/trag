using Trag.Domain.Stash;

namespace Trag.Domain.Presentation.Text;

// What the stash list, its preview and its questions say about one entry, and the hint for a stash left unnamed.
public static class StashText
{
    // The sidebar button before the list has arrived; then it names the newest stash.
    public const string Button = "Stashes";
    public static string DefaultHint(string? branch) => "Left empty, Git names it \"WIP on " + (branch ?? "the current branch") + "\".";
    public static string Detail(StashEntry entry, DateTimeOffset now) => (entry.Branch is { } branch ? branch + " · " : "") + RelativeTime.Opened(entry.When, now);
    public static string Tooltip(StashEntry entry) => entry.Reference + " · " + entry.Message;
    public static string DropQuestion(StashEntry entry) => "Remove " + entry.Reference + " · " + entry.Title;
    public static string PreviewTitle(StashEntry entry) => "Stash " + entry.Reference + " · " + entry.Title;
}
