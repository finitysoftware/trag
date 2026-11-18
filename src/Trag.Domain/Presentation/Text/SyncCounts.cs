using Trag.Domain.Sync;

namespace Trag.Domain.Presentation.Text;

// How far the branch is from its upstream, the toolbar labels that carry the counts, when it was last fetched, and why
// Pull is off for a branch rewritten after it was pushed.

public sealed record SyncCounts(int? Ahead, int? Behind, string Text, string FetchedText)
{
    public const string PullTipText = "Fetch and merge the selected branch. Requires clean working files and index (Ctrl+Shift+L).";

    // Set for a branch rewritten after it was pushed: the commits it is behind are its own old ones.
    public string? RewrittenUpstream { get; init; }
    public bool HasPullCount => Behind is > 0;
    // The count asks for a pull only when pulling would bring in someone else's commits.
    public bool SuggestsPull => HasPullCount && RewrittenUpstream is null;
    public string PullTip => RewrittenUpstream is { } upstream
        ? "Pull is off: this branch was rewritten after it was pushed, so pulling would merge its old commits from " + upstream
            + " back in. To replace " + upstream + ", use Force push with lease in File status."
        : PullTipText;
    public bool HasPushCount => Ahead is > 0;
    public bool HasFetchedText => FetchedText.Length != 0;
    public string PullLabel => HasPullCount ? "Pull · " + Counted.Number(Behind!.Value) : "Pull";
    public string PushLabel => HasPushCount ? "Push · " + Counted.Number(Ahead!.Value) : "Push";

    private const string Unknown = "Ahead / behind unknown";

    public static SyncCounts Empty() => new(null, null, Unknown, "");

    // Only a real fetch earns a line; the repository's remotes say when it was, or that there was none.
    public static SyncCounts From(RepositorySyncState state, DateTimeOffset? lastFetch, DateTimeOffset now) =>
        new(state.Ahead, state.Behind, Describe(state), lastFetch is { } fetched ? "Fetched " + RelativeTime.Fetched(fetched, now) : "")
        {
            RewrittenUpstream = state.IsRewritten ? state.UpstreamName : null
        };

    private static string Describe(RepositorySyncState state)
    {
        if (state.Branch is { IsNew: true } branch)
            return branch.Display + " is not on " + state.Remote.Name + " yet";
        if (state.Ahead is { } ahead && state.Behind is { } behind)
            return $"↑ {ahead} ahead  ·  ↓ {behind} behind";
        return Unknown;
    }
}
