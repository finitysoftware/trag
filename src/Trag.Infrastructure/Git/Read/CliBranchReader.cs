using System.Globalization;
using System.Text;
using Trag.Application.Ports.Git;
using Trag.Domain;
using Trag.Domain.Branches;
using Trag.Domain.Merge;
using Trag.Domain.Naming;
using Trag.Domain.Paths;
using Trag.Domain.Rebase;
using Trag.Domain.Sync;
using Trag.Domain.Tags;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// Every local and remote branch, and which one is checked out, from one for-each-ref call, bounded by the limit and
// sorted by Git, the checked-out branch first so the limit never cuts it off and aliases such as origin/HEAD last so they
// take no branch's place under it. The output is at most one branch more than the limit, so decoding it whole is bounded
// too. The tags are read the same way, and what a merge between two branches, or a rebase of the current one onto
// another, would do is read here too.
public sealed class CliBranchReader(GitProcessRunner runner) : IBranchReader
{
    private readonly PresetGitRunner objects = new(runner, LocalGitPreset.Writing);
    private MergePreviewReader Merges => new(objects);
    private RebasePreviewReader Rebases => new(objects);

    public Task<MergePreview> PreviewMergeAsync(string root, string into, string from, CancellationToken ct) =>
        WithinTimeAsync(token => Merges.ReadAsync(root, into, from, token), ct);

    public Task<RebasePreview> PreviewRebaseAsync(string root, string onto, CancellationToken ct) =>
        WithinTimeAsync(token => Rebases.ReadAsync(root, onto, token), ct);

    public Task<BranchSet> ReadBranchesAsync(string root, CancellationToken ct) => WithinTimeAsync(async token =>
    {
        var refs = await objects.RunAsync(root, ["for-each-ref", "--count=" + (GitLimits.BranchCount + 1), "--sort=refname", "--sort=symref", "--sort=-HEAD",
            "--format=%(refname)%00%(objectname)%00%(upstream)%00%(symref)%00%(HEAD)", "refs/heads", "refs/remotes"], token).ConfigureAwait(false);
        RemoteRefspecMapping[]? mappings = null;
        var local = new List<BranchInfo>();
        var remote = new List<BranchInfo>();
        string? current = null;
        var branches = 0;
        foreach (var row in Encoding.UTF8.GetString(refs.StandardOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var (name, id, upstream, symref, isHead) = BranchRefParser.Parse(row);
            if (symref.Length != 0) continue; // origin/HEAD is an alias, not a branch.
            if (++branches > GitLimits.BranchCount) break;
            if (!GitObjectId.IsValid(id)) throw new FormatException("Invalid branch object ID.");
            if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                local.Add(new(name[11..], name, id, false, null, upstream.Length == 0 ? null : upstream));
                if (isHead) current = name[11..];
            }
            else if (RemoteBranch(name, id) is { } remoteBranch)
            {
                // Only a remote branch needs the fetch mappings, so a repository without one never reads them.
                mappings ??= await FetchMappingsAsync(root, token).ConfigureAwait(false);
                remote.Add(remoteBranch with { NameOnRemote = NameOnRemote(mappings, name) });
            }
        }
        // An unborn branch is not listed; the caller names it from the snapshot.
        return new BranchSet(local, remote, current, branches > GitLimits.BranchCount);
    }, ct);

    // A lightweight tag names its commit; an annotated one names a tag object whose target Git peels (*objectname). Git
    // 2.43 peels it only once, so there a tag of a tag still names a tag, whose commit cat-file reads. A tag of anything
    // but a commit is no tag of the history, so it is left out.
    public Task<IReadOnlyList<TagInfo>> ReadTagsAsync(string root, CancellationToken ct) => WithinTimeAsync<IReadOnlyList<TagInfo>>(async token =>
    {
        var refs = await objects.RunAsync(root, ["for-each-ref", "--count=" + GitLimits.BranchCount, "--sort=-v:refname",
            "--format=%(refname)%00%(objecttype)%00%(objectname)%00%(*objecttype)%00%(*objectname)%00%(contents:subject)", "refs/tags"], token).ConfigureAwait(false);
        var rows = Encoding.UTF8.GetString(refs.StandardOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(row => row.Split('\0')).ToArray();
        var innerTags = rows.Select(row => row is [_, _, _, "tag", var inner, _] ? inner : null).OfType<string>().Distinct().ToArray();
        var peeled = await CommitsOfAsync(root, innerTags, token).ConfigureAwait(false);
        var tags = new List<TagInfo>();
        foreach (var row in rows)
        {
            if (row is not [var name, var type, var id, var peeledType, var peeledId, var subject] || !name.StartsWith("refs/tags/", StringComparison.Ordinal)) continue;
            var commit = type == "commit" ? id : peeledType == "commit" ? peeledId : peeledType == "tag" ? peeled.GetValueOrDefault(peeledId) : null;
            if (commit is null) continue;
            if (!GitObjectId.IsValid(commit)) throw new FormatException("Invalid tag object ID.");
            tags.Add(new(name["refs/tags/".Length..], commit, subject));
        }
        return tags;
    }, ct);

    // The commit each tag object ends at, from one cat-file call that answers the tags in order; one that ends at
    // anything else has no answer.
    private async Task<Dictionary<string, string>> CommitsOfAsync(string root, string[] tags, CancellationToken ct)
    {
        var answers = await objects.RunAsync(root, ["cat-file", "--batch-check=%(objectname)"], ct,
            Encoding.ASCII.GetBytes(string.Concat(tags.Select(tag => tag + "^{commit}\n")))).ConfigureAwait(false);
        return tags.Zip(Encoding.ASCII.GetString(answers.StandardOutput).Split('\n')).Where(pair => GitObjectId.IsValid(pair.Second))
            .ToDictionary(pair => pair.First, pair => pair.Second);
    }

    public Task<bool> ContainsAsync(string root, string commit, CancellationToken ct) => WithinTimeAsync(async token =>
        (await objects.ResultAsync(root, ["merge-base", "--is-ancestor", commit, "HEAD"], token).ConfigureAwait(false)).ExitCode == 0, ct);

    public Task<int?> DistanceAsync(string root, string commit, CancellationToken ct) => WithinTimeAsync<int?>(async token =>
    {
        if ((await objects.ResultAsync(root, ["merge-base", "--is-ancestor", commit, "HEAD"], token).ConfigureAwait(false)).ExitCode != 0) return null;
        var count = await objects.ResultAsync(root, ["rev-list", "--count", commit + "..HEAD"], token).ConfigureAwait(false);
        return count.ExitCode == 0 && int.TryParse(Encoding.UTF8.GetString(count.StandardOutput).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var distance)
            ? distance : null;
    }, ct);

    // Compared from the commit to the index, a file the index lacks is one the index deleted.
    public Task<IReadOnlyList<GitPath>> ReadNotInIndexAsync(string root, string commit, CancellationToken ct) => WithinTimeAsync<IReadOnlyList<GitPath>>(async token =>
        NulRecordReader.Records((await objects.RunAsync(root, ["diff", "--cached", "--name-only", "-z", "--no-renames", "--diff-filter=D", commit], token).ConfigureAwait(false)).StandardOutput)
            .Select(path => new GitPath(path)).ToList(), ct);

    // The commit's first parent is the base, and the first commit has none; the commits after it come oldest first with whole
    // messages, merges counted apart. Git shows no parent for the oldest commit of a shallow clone either, while the commit
    // itself still names it.
    public Task<RewriteRange?> ReadRewriteAsync(string root, string commit, CancellationToken ct) => WithinTimeAsync<RewriteRange?>(async token =>
    {
        if ((await objects.ResultAsync(root, ["merge-base", "--is-ancestor", commit, "HEAD"], token).ConfigureAwait(false)).ExitCode != 0) return null;
        var parents = Encoding.UTF8.GetString((await objects.RunAsync(root, ["rev-list", "--parents", "-n", "1", commit], token).ConfigureAwait(false)).StandardOutput)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var start = parents.Length < 2 ? null : parents[1];
        var range = start is null ? "HEAD" : start + "..HEAD";
        var merges = int.Parse(Encoding.UTF8.GetString((await objects.RunAsync(root, ["rev-list", "--count", "--min-parents=2", range, "--"], token).ConfigureAwait(false)).StandardOutput).Trim(),
            NumberStyles.None, CultureInfo.InvariantCulture);
        var log = Encoding.UTF8.GetString((await objects.RunAsync(root, ["log", "--reverse", "-z", "--no-show-signature", "--encoding=UTF-8", "--format=%H%x1f%s%x1f%B", range, "--"], token).ConfigureAwait(false)).StandardOutput);
        var steps = log.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(record => record.Split('\x1f', 3))
            .Select(fields => Pick(fields[0], fields[1], fields[2].TrimEnd('\n'))).ToArray();
        return new(start, steps, merges) { StartsAtShallowBoundary = start is null && await NamesParentAsync(root, commit, token).ConfigureAwait(false) };
    }, ct);

    private static RewriteStep Pick(string id, string subject, string message) => new(id, subject, message, RewriteAction.Pick, message);

    private async Task<bool> NamesParentAsync(string root, string commit, CancellationToken ct) =>
        CommitObject.NamesParent((await objects.RunAsync(root, ["cat-file", "commit", commit], ct).ConfigureAwait(false)).StandardOutput);

    public Task<string?> RemoteUrlAsync(string root, string remote, CancellationToken ct) => WithinTimeAsync<string?>(async token =>
    {
        var result = await objects.ResultAsync(root, ["config", "--local", "--get", "remote." + remote + ".url"], token).ConfigureAwait(false);
        return result.ExitCode == 0 ? Encoding.UTF8.GetString(result.StandardOutput).TrimEnd('\r', '\n') : null;
    }, ct);

    // Every read here runs off the caller's thread and gives up after 30 seconds, besides the caller's own cancellation.
    private static Task<T> WithinTimeAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken ct) => ReadTimeout.RunAsync(TimeSpan.FromSeconds(30), read, ct);

    // refs/remotes/<remote>/<branch>: a ref without both parts is not a branch the picker can show. The remote is taken up to
    // the first slash; BranchCatalog moves the branch to a configured remote whose name holds a slash.
    private static BranchInfo? RemoteBranch(string name, string id)
    {
        if (!name.StartsWith("refs/remotes/", StringComparison.Ordinal)) return null;
        var shortName = name[13..];
        var slash = shortName.IndexOf('/');
        if (slash <= 0 || slash == shortName.Length - 1) return null;
        return new(shortName, name, id, true, shortName[..slash]);
    }

    // Every remote's fetch refspecs, each read for its own remote as a fetch reads them.
    private async Task<RemoteRefspecMapping[]> FetchMappingsAsync(string root, CancellationToken ct)
    {
        var result = await objects.RunAsync(root, ["config", "--null", "--get-regexp", "^remote\\..*\\.fetch$"], ct, allowMissing: true).ConfigureAwait(false);
        return [.. new GitConfiguration(GitConfigParser.Parse(result.StandardOutput)).Entries
            .Select(entry => RemoteRefspecMapping.Parse(entry.Value, entry.Key["remote.".Length..^".fetch".Length])).OfType<RemoteRefspecMapping>()];
    }

    private static string? NameOnRemote(RemoteRefspecMapping[] mappings, string tracking) =>
        mappings.Select(mapping => mapping.SourceFor(tracking)).FirstOrDefault(source => source is not null) is { } source ? RefNames.LocalBranch(source) : null;
}
