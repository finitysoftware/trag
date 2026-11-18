using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Sync;

// After a push the remote verified, the remote-tracking ref follows the pushed commit, and after a deletion it goes, when
// the fetch endpoint is the one that received it. With the value it was seen at, the ref moves only if it still holds that
// (compare and swap); one that moved meanwhile is left for the next fetch.
internal static class RemoteTracking
{
    public static async Task RecordArrivalAsync(PresetGitRunner git, string root, RemoteConfiguration config, string trackingRef, string head,
        string? seen, CancellationToken ct)
    {
        if (config.EffectiveFetch != config.EffectivePush) return;
        _ = await git.ResultAsync(root, seen is null ? ["update-ref", trackingRef, head] : ["update-ref", trackingRef, head, seen], ct).ConfigureAwait(false);
    }

    public static async Task RecordDeletionAsync(PresetGitRunner git, string root, RemoteConfiguration config, string trackingRef, CancellationToken ct)
    {
        if (config.EffectiveFetch != config.EffectivePush) return;
        _ = await git.ResultAsync(root, ["update-ref", "-d", trackingRef], ct).ConfigureAwait(false);
    }
}
