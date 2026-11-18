using Trag.Domain.Naming;

namespace Trag.Domain.History;

// One page of history: Text matches SHA, full message or author (empty lists every commit), PageIndex is zero-based,
// AllBranches walks every local and remote branch together with HEAD instead of the current branch only. Upstream is
// the upstream's commit, walked with HEAD when only the current branch is shown, so commits others pushed show too.
// Path, when set, keeps only the commits that change that file, followed across renames. Pages reads that many pages
// from PageIndex on at once, as the history shown is read again in place.
public sealed record HistoryQuery(string Text = "", int PageIndex = 0, bool AllBranches = false)
{
    public string? Upstream { get; init; }
    public string? Path { get; init; }
    public int Pages { get; init; } = 1;

    // What the text must be before Git sees it: bounded, and without NUL, which the -z output could not carry.
    public static bool IsValidText(string text) => text.Length <= GitLimits.QueryLength && !text.Contains('\0');
}
