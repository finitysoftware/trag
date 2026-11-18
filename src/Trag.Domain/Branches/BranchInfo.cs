namespace Trag.Domain.Branches;

// Ref names come from Git itself; presentation never turns free text into a revision.
public sealed record BranchInfo(string Name, string Ref, string Head, bool IsRemote, string? Remote = null, string? Upstream = null)
{
    // The name on the remote: "origin/feature" is "feature" on origin. A local branch is its own local name.
    public string LocalName => IsRemote && Remote is { } remote && Name.Length > remote.Length + 1 ? Name[(remote.Length + 1)..] : Name;
    // A remote branch's name on the remote, as the remote's fetch mappings say: "origin/feature/x" is "x" on origin when
    // origin fetches refs/heads/* into refs/remotes/origin/feature/*. None when no mapping writes this ref.
    public string? NameOnRemote { get; init; }

    public bool Matches(string query) => Name.Contains(query, StringComparison.OrdinalIgnoreCase);
}
