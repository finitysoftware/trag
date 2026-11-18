using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;
using Trag.Domain.Paths;

namespace Trag.Infrastructure.Git.Read;

// What this repository is that Trag does not write to yet, in the order the shell shows it. Each sentence names the
// state and says what is not supported; none of them stops the read.
internal sealed class RepositoryLimitationScanner(PresetGitRunner git)
{
    // What the index answers, kept until the index or the attributes outside it change. ls-files lists the index's
    // entries and check-attr --cached takes the attributes from it, and also from info/attributes and the global
    // attributes file, so the fingerprint that every read computes anyway and those two files say whether the answers
    // still hold. A repository the index says nothing about is a full scan again, like the first read of any repository.
    private sealed record IndexFacts(string GitDirectory, string Fingerprint, bool PartialClone, string Attributes, bool Lfs);
    private IndexFacts? facts;
    // The global attributes file is where Git says it is, asked again only when core.attributesFile changes.
    private (string? Configured, string Path)? globalAttributes;

    // The limitations, and whether the index holds Git LFS files at all.
    public async Task<(List<string> Limitations, bool LfsFiles)> ScanAsync(string root, string gitDir, string commonDir, string fingerprint,
        GitConfiguration config, bool lfsInstalled, CancellationToken ct)
    {
        var limitations = new List<string>();
        if (Directory.EnumerateFiles(gitDir, IndexTransaction.NamePattern).Any() && RecoveryLimitation(Path.Combine(gitDir, "index.lock")) is { } recovery)
            limitations.Add(recovery);
        if (!SamePath(gitDir, commonDir)) limitations.Add(LimitationText.LinkedWorktree);
        if (config.Flag("core.sparsecheckout")) limitations.Add(LimitationText.SparseCheckout);
        var partialClone = IsPartialClone(config);
        if (partialClone) limitations.Add(LimitationText.PartialClone);
        var index = await ReadIndexAsync(root, gitDir, fingerprint, partialClone, await AttributesOutsideTheIndexAsync(root, commonDir, config, ct), ct);
        if (index.Lfs && !lfsInstalled)
            limitations.Add(LimitationText.LfsNotInstalled);
        return (limitations, index.Lfs);
    }

    private async Task<IndexFacts> ReadIndexAsync(string root, string gitDir, string fingerprint, bool partialClone, string attributes, CancellationToken ct)
    {
        if (facts is { } known && known.GitDirectory == gitDir && known.Fingerprint == fingerprint && known.PartialClone == partialClone
            && known.Attributes == attributes) return known;
        // A large repository's index and its attributes are more than the runner holds, so both are read as they arrive.
        var paths = await git.StreamAsync(root, ["ls-files", "--stage", "-z"], LfsFilterRecords.PathsAsync, ct);
        var lfs = !partialClone && paths.Length > 0
            && await git.StreamAsync(root, ["check-attr", "--cached", "-z", "--stdin", "filter"], LfsFilterRecords.CountLfsAsync, ct, paths) > 0;
        return facts = new(gitDir, fingerprint, partialClone, attributes, lfs);
    }

    private async Task<string> AttributesOutsideTheIndexAsync(string root, string commonDir, GitConfiguration config, CancellationToken ct)
    {
        var configured = config.Value("core.attributesfile");
        if (globalAttributes is not { } known || known.Configured != configured)
            globalAttributes = known = (configured, GitBytes.Text((await git.ResultAsync(root, ["var", "GIT_ATTR_GLOBAL"], ct)).StandardOutput));
        return Content(Path.Combine(commonDir, "info", "attributes")) + "\0" + Content(Path.Combine(root, known.Path));
    }

    private static string Content(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

    // Without a lock, a private index is one Trag kept because it could not reconcile a write. A live transaction holds
    // index.lock open with no sharing, and its private index is never mistaken for recovery work.
    // A lock that opens with every kind of sharing, which Trag's own live write never allows but a running Git's lock
    // does, is taken beside a private index as the one a Trag ended mid-write left; the Git that Trag started may have
    // gone on to finish the write in the private index.
    private static string? RecoveryLimitation(string lockPath)
    {
        if (!File.Exists(lockPath)) return LimitationText.RecoveryIndex;
        try { new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete).Dispose(); }
        catch (IOException) { return null; }
        return LimitationText.InterruptedWrite;
    }

    // Either way of configuring one: the repository extension, or a remote that promises objects on demand.
    private static bool IsPartialClone(GitConfiguration config) => config.Value("extensions.partialclone") is not null
        || config.Entries.Any(entry => entry.Key.EndsWith(".promisor", StringComparison.Ordinal) && config.Flag(entry.Key));

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        LocalPaths.Comparison);
}
