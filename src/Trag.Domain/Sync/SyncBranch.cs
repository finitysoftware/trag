using Trag.Domain.Naming;

namespace Trag.Domain.Sync;

public sealed record SyncBranch(string SourceRef, string TrackingRef)
{
    // The remote does not have this branch yet; Push creates it under the same name and records the upstream.
    public bool IsNew { get; init; }
    public string Display => RefNames.LocalBranch(SourceRef) ?? SourceRef;
}
