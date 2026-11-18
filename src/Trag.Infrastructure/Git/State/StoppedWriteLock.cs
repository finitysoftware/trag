using Trag.Domain.Status;
using Trag.Infrastructure.Platform.Storage;

namespace Trag.Infrastructure.Git.State;

// The locks a write's own Git leaves when Cancel or a limit stops it. Git writes the index, a ref or packed-refs through a lock
// beside it, <file>.lock, and a Git that is killed never removes it, so every later write of that file would be refused
// as waiting on another program. A write names the files its Git writes; a fetch mapping's pattern stands for every ref
// whose name starts as the pattern does before its star. A lock of one of them goes only when the write was stopped
// and the lock was not there before the write began; one that was there, or one of a file the write does not name,
// belongs to another program and stays.
internal sealed class StoppedWriteLock
{
    private readonly string[] files;
    private readonly HashSet<string> foreign;

    public StoppedWriteLock(params string[] files)
    {
        this.files = files;
        foreign = [.. Locks()];
    }

    // The files an update of refs in a git directory writes: each ref, or packed-refs, with the files backend, and the
    // stack's table list, which a reftable repository writes for every ref instead.
    public static string[] RefStorage(string directory, params string[] refs) =>
        [.. refs.Select(name => Path.Combine(directory, name)), Path.Combine(directory, "reftable", "tables.list")];

    // The files a command that commits or moves HEAD writes: HEAD, AUTO_MERGE, which Git deletes afterwards in a
    // transaction of its own, and the root refs it names in the git directory; the branch HEAD is on, or the one a rebase
    // in progress rebases, packed-refs, which Git locks to delete a ref, and the notes an amend or a rebase copies to the
    // commits it rewrites under notes.rewriteRef, which Git keeps below refs/notes, in the common directory.
    public static string[] HeadStorage(RepositorySnapshot fresh, params string[] roots) =>
        [.. RefStorage(fresh.GitDirectory!, ["HEAD", "AUTO_MERGE", .. roots]),
            .. RefStorage(fresh.CommonDirectory!, "refs/heads/" + (fresh.Branch ?? fresh.Rebase?.Branch), "packed-refs", "refs/notes/*")];

    public void Release(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) Release();
    }

    // For a write that was stopped, whichever token stopped it.
    public void Release()
    {
        foreach (var path in Locks().Where(path => !foreign.Contains(path))) TemporaryFiles.Delete(path);
    }

    // Git rewrites packed-refs into packed-refs.new while it holds packed-refs.lock; that file is left as well.
    private IEnumerable<string> Locks() =>
        files.SelectMany(file => file.IndexOf('*') is var star and >= 0 ? Below(Path.GetFullPath(file[..star]))
            : Path.GetFileName(file) == "packed-refs" ? [file + ".lock", file + ".new"] : [file + ".lock"]).Where(File.Exists);

    private static IEnumerable<string> Below(string prefix) => Path.GetDirectoryName(prefix) is { } folder && Directory.Exists(folder)
        ? Directory.EnumerateFiles(folder, "*.lock", SearchOption.AllDirectories).Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
        : [];
}
