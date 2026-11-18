using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Stash;

// The repository's stashes as the sidebar shows them, newest first: the button names the newest and counts them once
// there is more than one.
public static class StashList
{
    public static string ButtonText(IReadOnlyList<StashEntry> stashes) => stashes.Count == 0 ? StashText.Button : stashes[0].Title;

    public static string CountText(IReadOnlyList<StashEntry> stashes) => stashes.Count > 1 ? Counted.Number(stashes.Count) : "";
}
