using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Sync;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.Git.Sync;

internal sealed class SyncBranchList(RemoteRefspecMapping[] mappings)
{
    private readonly List<SyncBranch> branches = [];
    private readonly Dictionary<string, string> tips = new(StringComparer.Ordinal);

    public void AddFetched(string row)
    {
        var (reference, tip, isSymbolic) = RemoteRefParser.Parse(row);
        if (isSymbolic) return;
        foreach (var mapping in mappings)
        {
            if (mapping.SourceFor(reference) is not { } source) continue;
            branches.Add(new(source, reference));
            tips[reference] = tip;
            return;
        }
    }

    public SyncBranch? MissingBranch(string? source, bool isNew)
    {
        if (source is null || branches.Any(branch => branch.SourceRef == source)) return null;
        foreach (var mapping in mappings)
            if (mapping.DestinationFor(source) is { } destination)
                return new(source, destination) { IsNew = isNew };
        return null;
    }

    public void Add(SyncBranch branch) => branches.Add(branch);
    public SyncBranch? Choose(string? source) => source is null
        ? branches.FirstOrDefault(branch => branch.IsNew)
        : branches.FirstOrDefault(branch => branch.SourceRef == source);
    public string? Tip(SyncBranch? branch) => branch is null ? null : tips.GetValueOrDefault(branch.TrackingRef);

    // At most the branch limit of them, the new branch first and the rest by name, and always the chosen one, which may
    // sort past the limit.
    public SyncBranch[] Visible(SyncBranch? chosen) => branches.Take(GitLimits.BranchCount)
        .Concat(chosen is null || branches.IndexOf(chosen) < GitLimits.BranchCount ? [] : [chosen]).OrderBy(branch => branch.IsNew ? 0 : 1)
        .ThenBy(branch => branch.Display, StringComparer.Ordinal).ToArray();

    public string Message(SyncRemote remote, SyncBranch? chosen)
    {
        if (remote.Limitation.Length != 0) return remote.Limitation;
        if (branches.Count > GitLimits.BranchCount) return SyncText.TooManyBranches;
        if (chosen is { IsNew: true }) return SyncText.NotOnRemoteYet(chosen.Display, remote.Name);
        if (chosen is null) return SyncText.ChooseUpstream;
        if (Tip(chosen) is null) return SyncText.UpstreamNotLocal;
        return SyncText.AheadBehind;
    }
}
