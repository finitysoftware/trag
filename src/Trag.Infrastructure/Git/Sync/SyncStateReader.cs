using Trag.Domain;
using Trag.Domain.Naming;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Sync;

// What the sidebar shows about one remote: which upstream branches exist, which one is selected, how far ahead and
// behind the current branch is, whether a branch that diverged was rewritten, and which account it would use. Nothing
// here contacts the remote: every answer comes from local refs, so it is instant and works offline.
internal sealed class SyncStateReader(PresetGitRunner git, RemoteConfigurationReader configuration,
    GitRefNameValidator refNames, RemoteAccountStore? accountStore)
{
    public async Task<RepositorySyncState> ReadStateAsync(RepositorySnapshot snapshot, string remote, string? selected, CancellationToken ct)
    {
        var config = await configuration.ReadConfigurationAsync(snapshot, remote, ct).ConfigureAwait(false);
        var branches = new SyncBranchList(config.Mappings);
        var refs = await git.RunAsync(snapshot.Root, ["for-each-ref", "--count=" + (GitLimits.BranchCount + 1), "--format=%(refname)%00%(objectname)%00%(symref)", "refs/remotes/" + remote + "/"], ct).ConfigureAwait(false);
        foreach (var row in GitBytes.StrictText(refs.StandardOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            branches.AddFetched(row);
        selected ??= snapshot.TrackingRemote == remote ? snapshot.UpstreamBranchRef : null;
        await AddMissingBranchAsync(snapshot.Root, branches, selected, false, ct).ConfigureAwait(false);
        var current = snapshot.Branch is { } name ? "refs/heads/" + name : null;
        await AddMissingBranchAsync(snapshot.Root, branches, current, true, ct).ConfigureAwait(false);
        var chosen = branches.Choose(selected);
        var tip = branches.Tip(chosen);
        var (ahead, behind) = await ReadCountsAsync(snapshot.Root, snapshot.Head, tip, ct).ConfigureAwait(false);
        var rewritten = ahead > 0 && behind > 0 && current is not null && tip is not null
            && await BranchHistory.WasTipAsync(git, snapshot.Root, current, tip, ct).ConfigureAwait(false);
        return new(config.Remote, branches.Visible(chosen), chosen, ahead, behind, branches.Message(config.Remote, chosen), tip)
        {
            Accounts = accountStore is null ? null : await config.ReadAccountAsync(accountStore, snapshot.Root, ct).ConfigureAwait(false),
            Rewritten = rewritten
        };
    }

    // Where one upstream stands now, how many of its commits the branch lacks, and whether the branch still holds
    // commits the upstream dropped when it was rewritten, from its remote-tracking ref and that ref's reflog alone; a
    // symbolic ref is an alias, not a branch, as in the full read.
    public async Task<(string? Tip, int? Behind, bool Rewritten)> ReadUpstreamAsync(RepositorySnapshot snapshot, SyncBranch branch, CancellationToken ct)
    {
        var refs = await git.RunAsync(snapshot.Root, ["for-each-ref", "--format=%(refname)%00%(objectname)%00%(symref)", branch.TrackingRef], ct).ConfigureAwait(false);
        var tip = GitBytes.StrictText(refs.StandardOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(RemoteRefParser.Parse)
            .Where(row => row.RefName == branch.TrackingRef && !row.IsSymref).Select(row => row.ObjectName).FirstOrDefault();
        var (ahead, behind) = await ReadCountsAsync(snapshot.Root, snapshot.Head, tip, ct).ConfigureAwait(false);
        var rewritten = ahead > 0
            && await UpstreamHistory.DroppedAsync(git, snapshot.Root, snapshot.Head!, branch.TrackingRef, tip!, ahead.Value, ct).ConfigureAwait(false);
        return (tip, behind, rewritten);
    }

    private async Task AddMissingBranchAsync(string root, SyncBranchList branches, string? source, bool isNew, CancellationToken ct)
    {
        var branch = branches.MissingBranch(source, isNew);
        if (branch is not null && await refNames.IsValidRefAsync(root, branch.SourceRef, false, ct).ConfigureAwait(false))
            branches.Add(branch);
    }

    private async Task<(int? Ahead, int? Behind)> ReadCountsAsync(string root, string? head, string? tip, CancellationToken ct)
    {
        if (head is null || tip is null) return (null, null);
        GitObjectId.Validate(head);
        GitObjectId.Validate(tip);
        var counts = await git.RunAsync(root, ["rev-list", "--left-right", "--count", head + "..." + tip, "--"], ct).ConfigureAwait(false);
        var values = GitBytes.StrictText(counts.StandardOutput).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (values.Length != 2) throw new FormatException("Invalid ahead/behind result.");
        return (int.Parse(values[0], System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(values[1], System.Globalization.CultureInfo.InvariantCulture));
    }
}
