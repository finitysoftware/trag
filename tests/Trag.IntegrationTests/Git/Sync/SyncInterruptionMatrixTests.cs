using System.Text;
using static Trag.IntegrationTests.Git.Sync.SyncFixture;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Sync;
using Trag.Infrastructure.Platform.Storage;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Sync;

// Every synchronization stopped where the stop is deterministic (SyncInterruption): whatever stopped it, the repository
// keeps no lock file and FETCH_HEAD stays as the terminal's fetch wrote it. A clone stopped in any of its steps leaves no
// folder, and a submodule update stopped while it clones leaves nothing the next update trips over. Leftovers that are
// not locks, such as objects/pack/tmp_pack_* of a fetch stopped mid-transfer, are not counted. The outcomes are in
// SyncInterruptionOutcomeTests.
public sealed class SyncInterruptionMatrixTests
{
    [Theory]
    [InlineData("fetch", "remote-waits")]
    [InlineData("pull-ff", "remote-waits")]
    [InlineData("push", "remote-waits")]
    [InlineData("force-push", "remote-waits")]
    [InlineData("publish", "remote-waits")]
    [InlineData("remote-branch-delete", "remote-waits")]
    [InlineData("remote-tag-push", "remote-waits")]
    [InlineData("remote-tag-delete", "remote-waits")]
    [InlineData("pull-lfs", "remote-waits")]
    [InlineData("push-lfs", "remote-waits")]
    [InlineData("fetch", "local-refs-locked")]
    [InlineData("fetch-tag", "local-refs-locked")]
    [InlineData("pull-ff", "local-refs-locked")]
    [InlineData("pull-ff", "local-refs-locked", "ORIG_HEAD")]
    [InlineData("pull-merge", "local-refs-locked")]
    [InlineData("pull-merge", "local-refs-locked", "AUTO_MERGE")]
    [InlineData("push", "local-refs-locked")]
    [InlineData("force-push", "local-refs-locked")]
    [InlineData("publish", "local-refs-locked")]
    [InlineData("remote-branch-delete", "local-refs-locked")]
    public async Task WhenASyncIsInterrupted_ShouldLeaveNoLockFileOrChangedFetchHeadInTheRepository(string operation, string point, string? held = null)
    {
        using var silent = new SilentServer();
        using var f = await SyncFixture.Create();
        var scenario = await SyncInterruption.PrepareAsync(f, operation, silent);
        var fetchHead = await File.ReadAllBytesAsync(FetchHead(f), f.Token);

        // A merge writes ORIG_HEAD and, for a merge commit, AUTO_MERGE, each in a transaction of its own before the branch;
        // AUTO_MERGE so from Git 2.45 on, as Git 2.43 writes it as a plain file without a lock.
        await (held is null ? scenario : scenario with { LocalRef = held }).StopAsync(f, point, silent);

        Assert.Empty(LocksLeft(f));
        Assert.Equal(fetchHead, await File.ReadAllBytesAsync(FetchHead(f), f.Token));
    }

    // The quiet limit stops a Git that says nothing as Cancel does, here while it holds the locks of One's refs: the fetch
    // is cancelled, and the push, which the remote took already, is verified with its tracking ref moved.
    [Theory]
    [InlineData("fetch", GitWriteOutcome.Cancelled)]
    [InlineData("push", GitWriteOutcome.Succeeded)]
    public async Task WhenTheQuietLimitStopsASyncWhileGitHoldsTheRefLocks_ShouldLeaveNoLockFile(string operation, GitWriteOutcome expected)
    {
        using var silent = new SilentServer();
        using var f = await SyncFixture.Create(quietLimit: TimeSpan.FromSeconds(5));
        var scenario = await SyncInterruption.PrepareAsync(f, operation, silent);

        var result = await scenario.StopAsync(f, "local-refs-quiet", silent);

        Assert.Equal(expected, result.Outcome);
        Assert.Empty(LocksLeft(f));
        if (expected == GitWriteOutcome.Succeeded) await scenario.Done!();
    }

    // A deletion that rewrites packed-refs writes packed-refs.new beside packed-refs.lock.
    private static IEnumerable<string> LocksLeft(SyncFixture f) => Directory.EnumerateFiles(Path.Combine(f.One, ".git"), "*.lock", SearchOption.AllDirectories)
        .Append(Path.Combine(f.One, ".git", "packed-refs.new")).Where(File.Exists).Select(path => Path.GetRelativePath(f.One, path));

    // Each step of a clone that talks to a server, stopped while the server keeps it waiting: the repository's own
    // transfer, the Git LFS download its .lfsconfig points at, and a submodule's clone.
    [Theory]
    [InlineData("transfer")]
    [InlineData("lfs-pull")]
    [InlineData("submodules")]
    public async Task WhenACloneIsInterruptedInAnyStep_ShouldLeaveNoFolderBehind(string step)
    {
        using var silent = new SilentServer();
        using var f = await GitFixture.CreateAsync("main");
        var source = step == "transfer" ? silent.Url("source.git") : await CloneSourceAsync(f, step, silent);
        var catalog = new RepositoryCatalogFacade(new GitProcessRunner(), new RepositoryCatalogStore(Path.Combine(f.Container, "repositories.json")));
        var target = Path.Combine(f.Container, "clones", "interrupted");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);

        var pending = catalog.CloneAsync(new(source, target, Submodules: step == "submodules"), cancel.Token);
        while (!silent.Connected.IsCompleted && !pending.IsCompleted) await Task.Delay(20, f.Token);
        Assert.True(silent.Connected.IsCompleted, "The clone never reached the server: " + (pending.IsCompletedSuccessfully ? (await pending).Message : ""));
        await cancel.CancelAsync();
        var result = await pending;

        Assert.Equal(CloneText.Canceled, result.Message);
        Assert.False(Directory.Exists(target));
    }

    // A repository whose clone reaches the silent server only in its own step: an LFS file whose content .lfsconfig puts
    // there, or a submodule whose address is there.
    private static async Task<string> CloneSourceAsync(GitFixture f, string step, SilentServer silent)
    {
        if (step == "lfs-pull")
        {
            await LfsConfig.SetAsync(f, f.Root, installed: true);
            await f.WriteAsync(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
            await f.WriteAsync("asset.bin", "fixture\n");
            await f.GitAsync("config", "--file", ".lfsconfig", "lfs.url", silent.Url("lfs"));
            await f.CommitAsync("Asset", ".gitattributes", "asset.bin", ".lfsconfig");
        }
        else
        {
            await f.CommitAsync("Base");
            await f.GitAsync("config", "--file", ".gitmodules", "submodule.sub.path", "sub");
            await f.GitAsync("config", "--file", ".gitmodules", "submodule.sub.url", silent.Url("sub.git"));
            await f.GitAsync("update-index", "--add", "--cacheinfo", "160000," + await f.GitAsync("rev-parse", "HEAD") + ",sub");
            await f.CommitAsync("Sub", ".gitmodules");
        }
        return f.Root;
    }

    // Git clones a missing submodule into .git/modules/<name> before it checks it out. Stopped while the module folder
    // has only its HEAD, or while a shallow clone holds shallow.lock, the update left a module folder without objects
    // and a gitfile pointing at it, and every later update failed with "Unable to find current revision".
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenASubmoduleUpdateIsCanceledWhileTheSubmoduleIsCloned_ShouldLetTheNextUpdateFinish(bool shallow)
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var clone = await CloneWithASlowSubmoduleAsync(f, shallow);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);

        var pending = UpdateSubmodulesAsync(f, clone, cancel.Token);
        var module = Path.Combine(clone, ".git", "modules", "sub");
        while (!File.Exists(Path.Combine(module, shallow ? "shallow.lock" : "HEAD")) && !pending.IsCompleted) await Task.Delay(5, f.Token);
        await cancel.CancelAsync();
        var stopped = await pending;
        var next = await UpdateSubmodulesAsync(f, clone, f.Token);

        Assert.Equal(GitWriteOutcome.Cancelled, stopped.Outcome);
        Assert.True(next.Outcome == GitWriteOutcome.Succeeded, next.Message + next.Details);
        Assert.Empty((await f.ReadAsync(clone)).SubmodulesToUpdate);
        // A module folder without objects reads as no submodule to update, so the submodule's own HEAD is what proves it.
        var checkedOut = await f.RunAtAsync(Path.Combine(clone, "sub"), "rev-parse", "HEAD");
        Assert.Equal(await f.GitAtAsync(clone, "rev-parse", "HEAD:sub"), Encoding.UTF8.GetString(checkedOut.StandardOutput).Trim());
    }

    // A submodule cloned before keeps the repository that holds its history when an update that only fetches into it
    // is stopped: its module folder, also after the user deleted the submodule's folder, or the repository cloned
    // straight into the submodule's folder, without a module folder. Only what a stopped update created goes.
    [Theory]
    [InlineData("module-without-folder")]
    [InlineData("repository-in-folder")]
    public async Task WhenAnUpdateOfAnAlreadyClonedSubmoduleIsCanceled_ShouldKeepItsRepository(string shape)
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var clone = Path.Combine(f.Container, "clone");
        var sub = Path.Combine(clone, "sub");
        var repository = shape == "module-without-folder" ? Path.Combine(clone, ".git", "modules", "sub") : Path.Combine(sub, ".git");
        await f.GitAsync("-c", "protocol.file.allow=always", "clone", shape == "module-without-folder" ? "--recurse-submodules" : "--no-recurse-submodules",
            "--", f.Root, clone);
        if (shape == "repository-in-folder") await f.GitAsync("clone", "--", SubmoduleFixture.Origin(f), sub);
        await f.GitAtAsync(clone, "config", "protocol.file.allow", "always");
        var checkedOut = await f.GitAtAsync(repository, "rev-parse", "HEAD");
        // The repository then records a submodule commit the clone's submodule does not have yet.
        await f.GitAtAsync(SubmoduleFixture.Origin(f), "commit", "--allow-empty", "-m", "Sub two");
        await f.GitAtAsync(Path.Combine(f.Root, "sub"), "pull", "--ff-only", "origin", "main");
        await f.GitAsync("add", "sub"); await f.GitAsync("commit", "-m", "Sub two");
        await f.GitAtAsync(clone, "pull", "--no-recurse-submodules", "--ff-only");
        var marker = Path.Combine(f.Container, "fetch-started"); var script = Path.Combine(f.Container, "slow-upload.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nsleep 30 & echo ready > " + Quote(marker) + "\nwait\nexec git-upload-pack \"$@\"\n",
            new UTF8Encoding(false), f.Token);
        await f.GitAtAsync(repository, "config", "remote.origin.uploadpack", "sh " + Quote(script));
        if (shape == "module-without-folder") Directory.Delete(sub, true);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);

        var pending = UpdateSubmodulesAsync(f, clone, cancel.Token);
        while (!File.Exists(marker) && !pending.IsCompleted) await Task.Delay(20, f.Token);
        await cancel.CancelAsync();
        var stopped = await pending;

        Assert.Equal(GitWriteOutcome.Cancelled, stopped.Outcome);
        Assert.True(File.Exists(Path.Combine(repository, "HEAD")), "The submodule's repository is gone: " + repository);
        Assert.Equal(checkedOut, await f.GitAtAsync(repository, "rev-parse", "HEAD"));
    }

    // A clone of the repository without its submodule, whose recorded submodule commit holds 20 MB that do not
    // compress, so that cloning the submodule takes long enough to be stopped half way. The clone allows the local
    // address of the submodule, as a user's own configuration may.
    private static async Task<string> CloneWithASlowSubmoduleAsync(GitFixture f, bool shallow)
    {
        var origin = SubmoduleFixture.Origin(f);
        var content = new byte[20 * 1024 * 1024];
        new Random(262).NextBytes(content);
        await File.WriteAllBytesAsync(Path.Combine(origin, "large.bin"), content, f.Token);
        await f.GitAtAsync(origin, "add", "large.bin"); await f.GitAtAsync(origin, "commit", "-m", "Large");
        await f.GitAtAsync(Path.Combine(f.Root, "sub"), "pull", "--ff-only", "origin", "main");
        if (shallow) await f.GitAsync("config", "--file", ".gitmodules", "submodule.sub.shallow", "true");
        await f.GitAsync("add", "sub", ".gitmodules"); await f.GitAsync("commit", "-m", "Large sub");
        var clone = Path.Combine(f.Container, "clone");
        await f.GitAsync("clone", "--", f.Root, clone);
        await f.GitAtAsync(clone, "config", "protocol.file.allow", "always");
        return clone;
    }

    private static async Task<GitWriteResult> UpdateSubmodulesAsync(GitFixture f, string root, CancellationToken ct) =>
        await new GitSyncFacade(f.Runner, new CliRepositoryReader(f.Runner)).ExecuteAsync(new SubmoduleUpdateRequest(await f.ReadAsync(root)), ct);

    private static string FetchHead(SyncFixture f) => Path.Combine(f.One, ".git", "FETCH_HEAD");
}
