using Trag.Domain.Accounts;
using Trag.Domain.Status;

namespace Trag.Domain.Branches;

// A branch read as the picker shows it: the current branch apart, the other local branches sorted, and the remote
// branches grouped by the remote of the snapshot they belong to, with groups and rows sorted case-insensitively and equal
// names in Git's order. A filter keeps the branches whose name contains the query and drops the groups that end up empty.
public sealed record BranchCatalog(BranchInfo? Current, IReadOnlyList<BranchInfo> Local, IReadOnlyList<RemoteBranchGroup> RemoteGroups)
{
    public static readonly BranchCatalog Empty = new(null, [], []);

    // Git listed more branches than the reader shows.
    public bool Truncated { get; init; }
    // The remotes the branches were read with, whose pages a branch's request opens, and the provider of the preferred
    // one, which names the request of a branch no page fits.
    public IReadOnlyList<RepositoryRemote> Remotes { get; init; } = [];
    public AccountProvider? RequestProvider { get; init; }
    public IEnumerable<BranchInfo> Remote => RemoteGroups.SelectMany(group => group.Branches);
    // What Enter in the filter switches to: the first local match, else the first remote one, else the current branch.
    public BranchInfo? FirstMatch => Local.FirstOrDefault() ?? Remote.FirstOrDefault() ?? Current;
    // The names a new branch cannot take.
    public IEnumerable<string> LocalNames => Current is null ? Local.Select(branch => branch.Name) : Local.Select(branch => branch.Name).Prepend(Current.Name);

    public static BranchCatalog Create(BranchSet branches, RepositorySnapshot snapshot)
    {
        var currentName = branches.Current ?? snapshot.Branch;
        var local = branches.Local.OrderBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var current = local.FirstOrDefault(branch => branch.Name == currentName);
        var others = local.Where(branch => !ReferenceEquals(branch, current)).ToArray();
        var groups = branches.Remote
            .Select(branch => OnItsRemote(branch, snapshot.Remotes))
            .GroupBy(branch => branch.Remote ?? "", StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RemoteBranchGroup(group.Key, group.OrderBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToArray();
        return new(current, others, groups)
        {
            Truncated = branches.Truncated, Remotes = snapshot.Remotes,
            RequestProvider = snapshot.PreferredRemote is { } preferred ? PullRequestPage.ProviderOf(preferred.PushAddress) : null
        };
    }

    // A remote may be named with a slash, so a remote branch belongs to the longest remote whose name, and a slash, its own
    // name starts with. A branch of a remote no longer configured keeps the remote the reader took from its name.
    private static BranchInfo OnItsRemote(BranchInfo branch, IReadOnlyList<RepositoryRemote> remotes) =>
        remotes.Where(remote => branch.Name.Length > remote.Name.Length + 1 && branch.Name.StartsWith(remote.Name + "/", StringComparison.Ordinal))
            .MaxBy(remote => remote.Name.Length) is { } owner ? branch with { Remote = owner.Name } : branch;

    public BranchCatalog Filter(string search)
    {
        var query = search.Trim();
        if (query.Length == 0)
            return this;
        var groups = RemoteGroups
            .Select(group => new RemoteBranchGroup(group.Remote, group.Branches.Where(branch => branch.Matches(query)).ToArray()))
            .Where(group => group.Branches.Count != 0)
            .ToArray();
        return new(Current?.Matches(query) == true ? Current : null, Local.Where(branch => branch.Matches(query)).ToArray(), groups) { Truncated = Truncated };
    }
}
