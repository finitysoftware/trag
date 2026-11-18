using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Sync;

namespace Trag.Application.Sync;

// What Git says about the sync target, as the sidebar and the toolbar show it: the state read, the counts and the target's
// lines, the account the remote uses, and why there is nothing more when there is not.
public sealed record SyncOverview(RepositorySyncState? State, SyncCounts Counts, SyncTarget Target,
    SyncAccountSelection Accounts, string Message)
{
    public bool CanSynchronize => State is { CanSynchronize: true } && Accounts.IsConfirmed;

    public static SyncOverview Empty(RepositoryRemote? remote = null) =>
        new(null, SyncCounts.Empty(), SyncTarget.Empty(remote), new(null), SyncText.NoRemote);

    public static SyncOverview From(RepositorySnapshot snapshot, RepositorySyncState state, DateTimeOffset? lastFetch, DateTimeOffset now) =>
        new(state, SyncCounts.From(state, lastFetch, now), SyncTarget.From(snapshot, state), new(state.Accounts), state.Message);

    public SyncOverview Reading() => this with { Message = SyncText.Reading };

    // The target was fetched at that time: the counts say so and keep what they count; an empty overview has no target
    // to say it about.
    public SyncOverview Fetched(DateTimeOffset at) => State is null ? this : this with { Counts = SyncCounts.From(State, at, at) };
}
