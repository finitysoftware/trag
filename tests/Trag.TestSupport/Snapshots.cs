using System.Text;
using Trag.Domain.History;
using Trag.Domain.Paths;
using Trag.Domain.Rebase;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.TestSupport;

// Builders for the Domain models every test needs; defaults describe a healthy repository on "main" with an identity.
public static class Snapshots
{
    public static readonly string HeadId = new('a', 40);
    public static readonly GitIdentity Identity = new("Fixture", "fixture@example.invalid");

    public static RepositorySnapshot Create(string root = "fixture", string? branch = "main", string? head = null, IReadOnlyList<FileStatus>? files = null,
        IReadOnlyList<RepositoryRemote>? remotes = null, string? trackingRemote = null, string? upstream = null, string? upstreamBranchRef = null,
        RepositoryOperation operation = RepositoryOperation.None, IReadOnlyList<string>? limitations = null, bool identity = true,
        RepositoryMerge? merge = null, string indexFingerprint = "index", int detachedCommits = 0, string? stashHead = null, bool unborn = false) =>
        new(root, "2.43.0", branch, unborn ? null : head ?? HeadId, upstream, trackingRemote, remotes ?? [], operation, limitations ?? [], files ?? [])
        {
            IndexFingerprint = indexFingerprint,
            Author = identity ? Identity : null,
            Committer = identity ? Identity : null,
            Merge = merge,
            DetachedCommits = branch is null ? detachedCommits : 0,
            StashHead = stashHead,
            UpstreamBranchRef = upstreamBranchRef ?? (trackingRemote is not null && upstream is not null ? "refs/heads/" + upstream[(upstream.IndexOf('/') + 1)..] : null)
        };

    public static GitPath Path(string path) => new(Encoding.UTF8.GetBytes(path));
    public static FileStatus Modified(string path) => new(Path(path), '.', 'M');
    public static FileStatus Staged(string path, char index = 'M') => new(Path(path), index, '.');
    public static FileStatus PartiallyStaged(string path) => new(Path(path), 'M', 'M');
    public static FileStatus Untracked(string path) => new(Path(path), '?', '?');
    public static FileStatus Renamed(string from, string to, char worktree = '.') => new(Path(to), 'R', worktree, Path(from));
    public static FileStatus Conflict(string path) => new(Path(path), 'U', 'U', IsConflict: true);

    public static RepositoryRemote Remote(string name = "origin", string? url = null) =>
        new(name, url ?? "https://fetch.example.invalid/" + name, url ?? "https://fetch.example.invalid/" + name);

    public static HistoryCommit Commit(string id, string subject, params string[] parents) =>
        new(id, "Author", "author@example.invalid", "2026-09-14T12:00:00+00:00", subject) { Parents = parents };

    public static string Id(char fill) => new(fill, 40);

    // A rebase of feature onto main stopped at the given step of the plan; each commit is Id('1'), Id('2')….
    public static RepositoryRebase Rebase(int step = 1, int total = 3, string limitation = "", string branch = "feature", string? ontoName = "main")
    {
        var commits = Enumerable.Range(1, total).Select(i => new RebaseCommit("pick", Id((char)('0' + i)), "Commit " + i)).ToArray();
        return new(branch, Id('c'), Id('d'), step, commits[..step], commits[step..], commits[step - 1].Id,
            "Commit " + step + "\n\n# Conflicts:\n#\tf.txt\n", "rebase-" + step, limitation) { OntoName = ontoName };
    }

    // A merge of origin/dev into main stopped with a conflict in a.
    public static RepositorySnapshot Merging() => Create(files: [Conflict("a")], operation: RepositoryOperation.Merge,
        merge: new(Id('b'), HeadId, "Merge remote-tracking branch 'origin/dev' into main", "fp", ""));

    public static RepositorySnapshot Rebasing(RepositoryRebase? rebase = null, IReadOnlyList<FileStatus>? files = null) =>
        Create(branch: null, operation: RepositoryOperation.Rebase, files: files) with { Rebase = rebase ?? Rebase() };
}
