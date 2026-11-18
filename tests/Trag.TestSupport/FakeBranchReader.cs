using Trag.Application.Ports.Git;
using Trag.Domain.Branches;
using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Domain.Rebase;
using Trag.Domain.Tags;

namespace Trag.TestSupport;

public sealed class FakeBranchReader : IBranchReader
{
    public List<string> Local { get; } = [];
    public List<string> Remote { get; } = [];
    public List<TagInfo> Tags { get; } = [];
    // The commits HEAD has; any other it has not.
    public List<string> Contained { get; } = [];
    // How many commits HEAD has after each commit it has; any other it has not.
    public Dictionary<string, int> Distances { get; } = [];
    // What a rewrite from each commit HEAD has would take; any other it has not.
    public Dictionary<string, RewriteRange> Rewrites { get; } = [];
    // The files each commit tracks that the index does not; none for any other commit.
    public Dictionary<string, IReadOnlyList<GitPath>> NotInIndex { get; } = [];
    // Set, every read of the commits to rewrite fails with it, as Git's output limit or the read's time limit would.
    public Exception? RewriteFailure { get; set; }
    // Set, the reads a question asks before it opens (how far the branch goes back, whether it has a commit) fail with it.
    public Exception? QuestionFailure { get; set; }
    // Each remote's address as configured.
    public Dictionary<string, string> RemoteUrls { get; } = [];
    // Set, every read of a remote's configured address fails with it, as the read's time limit would.
    public Exception? RemoteUrlFailure { get; set; }
    public int TagCalls { get; private set; }
    public Dictionary<string, string> Heads { get; } = [];
    // Each local branch's upstream ref; main tracks origin/main unless it is set here.
    public Dictionary<string, string> Upstreams { get; } = [];
    public string? Current { get; set; } = "main";
    public bool Truncated { get; set; }
    public int Calls { get; private set; }
    public string? LastRoot { get; private set; }
    public AsyncGate Gate { get; } = new();
    // A merge preview per (into, from) pair of refs; by default one commit to bring in that Git can fast-forward.
    public Func<string, string, MergePreview> Preview { get; set; } = (_, _) => new(Snapshots.HeadId, Snapshots.Id('b'), [], 1, 0, []);
    public List<(string Into, string From)> Previews { get; } = [];
    // A rebase preview per onto ref; by default one commit of its own replayed onto one new commit.
    public Func<string, RebasePreview> RebasePreview { get; set; } = _ => new(Snapshots.HeadId, Snapshots.Id('b'), [], 1, 1, [], null, 0);
    public List<string> RebasePreviews { get; } = [];
    public AsyncGate PreviewGate { get; } = new();

    public async Task<BranchSet> ReadBranchesAsync(string root, CancellationToken ct)
    {
        Calls++; LastRoot = root;
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        return new(
            Local.Select(name => new BranchInfo(name, "refs/heads/" + name, Heads.GetValueOrDefault(name, Snapshots.HeadId), false, null, (Upstreams.TryGetValue(name, out var upstream) ? upstream : name == "main" ? "refs/remotes/origin/main" : null))).ToArray(),
            Remote.Select(name => new BranchInfo(name, "refs/remotes/" + name, Heads.GetValueOrDefault(name, Snapshots.HeadId), true, name.Split('/')[0]) { NameOnRemote = name[(name.IndexOf('/') + 1)..] }).ToArray(),
            Current, Truncated);
    }

    public Task<bool> ContainsAsync(string root, string commit, CancellationToken ct) =>
        QuestionFailure is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(Contained.Contains(commit));
    public Task<string?> RemoteUrlAsync(string root, string remote, CancellationToken ct) =>
        RemoteUrlFailure is { } failure ? Task.FromException<string?>(failure) : Task.FromResult(RemoteUrls.GetValueOrDefault(remote));
    public Task<int?> DistanceAsync(string root, string commit, CancellationToken ct) =>
        QuestionFailure is { } failure ? Task.FromException<int?>(failure) : Task.FromResult(Distances.TryGetValue(commit, out var distance) ? distance : (int?)null);
    public Task<IReadOnlyList<GitPath>> ReadNotInIndexAsync(string root, string commit, CancellationToken ct) =>
        Task.FromResult(NotInIndex.GetValueOrDefault(commit) ?? []);
    public Task<RewriteRange?> ReadRewriteAsync(string root, string commit, CancellationToken ct) =>
        RewriteFailure is { } failure ? Task.FromException<RewriteRange?>(failure) : Task.FromResult(Rewrites.GetValueOrDefault(commit));

    public async Task<IReadOnlyList<TagInfo>> ReadTagsAsync(string root, CancellationToken ct)
    {
        TagCalls++;
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        return Tags.ToArray();
    }

    public async Task<RebasePreview> PreviewRebaseAsync(string root, string onto, CancellationToken ct)
    {
        RebasePreviews.Add(onto);
        await PreviewGate.WaitAsync(ct).ConfigureAwait(false);
        return RebasePreview(onto);
    }

    public async Task<MergePreview> PreviewMergeAsync(string root, string into, string from, CancellationToken ct)
    {
        Previews.Add((into, from));
        await PreviewGate.WaitAsync(ct).ConfigureAwait(false);
        return Preview(into, from);
    }
}
