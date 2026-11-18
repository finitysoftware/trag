namespace Trag.Domain.Branches;

// Git validates the name (check-ref-format) and the start point (rev-parse) before anything changes.
// Track names a remote-tracking ref such as refs/remotes/origin/feature for a new local branch.
// ForceAt deletes a branch Git considers unmerged, while it is still at the tip the user agreed to lose.
// For a checkout, Name is the commit id and HEAD is detached there.
public sealed record BranchOperation(string Name, string? StartPoint = null, bool Switch = true, string? Track = null, string? ForceAt = null);
