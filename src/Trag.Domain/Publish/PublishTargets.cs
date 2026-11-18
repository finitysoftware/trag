using Trag.Domain.Branches;

namespace Trag.Domain.Publish;

// The branches a publish may push into on one remote: the current branch first, new there unless the remote already has
// it, then the remote's other branches once each and sorted, by their names on the remote. The index is built once per
// branch read.
public static class PublishTargets
{
    public static IReadOnlyDictionary<string, string[]> Index(IEnumerable<BranchInfo> remoteBranches)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var group in remoteBranches.GroupBy(branch => branch.Remote))
        {
            if (group.Key is null)
                continue;
            result[group.Key] = group.Select(branch => branch.NameOnRemote).OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        return result;
    }

    public static IReadOnlyList<PublishTarget> For(IReadOnlyDictionary<string, string[]> index, string? remote, string? branch)
    {
        if (remote is null || branch is null)
            return [];
        var existing = index.GetValueOrDefault(remote) ?? [];
        return [new(branch, remote, !existing.Contains(branch, StringComparer.Ordinal)),
            .. existing.Where(name => name != branch).Select(name => new PublishTarget(name, remote, false))];
    }
}
