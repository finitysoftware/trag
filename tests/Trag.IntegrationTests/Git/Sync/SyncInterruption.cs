using System.Text;
using static Trag.IntegrationTests.Git.Sync.SyncFixture;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Sync;

// One synchronization in a SyncFixture with something to bring in or send, run as the shell runs it (the status and the
// remote are read first, then the request goes to the synchronizer), and the points where a test stops it
// deterministically. Remote is how the remote keeps the operation waiting: a slow upload-pack, a pre-receive hook or a
// silent server. LocalRef and RemoteRef name the ref Git updates in One and in the bare remote, Verifying the progress
// line of the verification, and Done checks what a verified success leaves on both sides.
internal sealed record SyncInterruption(Func<CancellationToken, IProgress<string>?, Task<GitWriteResult>> Run, string Remote,
    string? LocalRef = null, string? RemoteRef = null, string? Verifying = null, Func<Task>? Done = null)
{
    public static async Task<SyncInterruption> PrepareAsync(SyncFixture f, string operation, SilentServer silent)
    {
        // FETCH_HEAD as the terminal's own fetch writes it; Trag never writes it.
        await f.Git(f.One, "fetch", "origin");
        switch (operation)
        {
            case "fetch":
                await f.Commit(f.Two, "incoming"); await f.Git(f.Two, "push", "origin", "trunk", "trunk:refs/heads/feature");
                return new(Sync(f, SyncAction.Fetch), "upload-pack", LocalRef: "refs/remotes/origin/");
            case "fetch-tag":
                // A commit pushed with its release tag: the fetch follows the tag in the transaction that moves the branch.
                await f.Commit(f.Two, "incoming"); await f.Git(f.Two, "tag", "-a", "v2", "-m", "v2");
                await f.Git(f.Two, "push", "origin", "trunk", "refs/tags/v2");
                return new(Sync(f, SyncAction.Fetch), "upload-pack", LocalRef: "refs/remotes/origin/");
            case "pull-ff":
                await f.Commit(f.Two, "incoming"); await f.Git(f.Two, "push", "origin", "trunk");
                return new(Sync(f, SyncAction.Pull), "upload-pack", LocalRef: "refs/heads/trunk", Verifying: MergeOutcomeText.Verifying,
                    Done: async () => Assert.Equal(await f.Head(f.Bare), await f.Head(f.One)));
            case "pull-merge":
                await f.Commit(f.Two, "incoming"); await f.Git(f.Two, "push", "origin", "trunk");
                await File.WriteAllTextAsync(Path.Combine(f.One, "local.txt"), "local\n", f.Token);
                await f.Git(f.One, "add", "local.txt"); await f.Git(f.One, "commit", "-m", "local");
                return new(Sync(f, SyncAction.Pull), "upload-pack", LocalRef: "refs/heads/trunk", Verifying: MergeOutcomeText.Verifying,
                    Done: async () => Assert.Equal(await f.Head(f.Bare), await f.Git(f.One, "rev-parse", "HEAD^2")));
            case "push":
                await f.Commit(f.One, "outgoing");
                return new(Sync(f, SyncAction.Push), "receive-pack", "refs/remotes/origin/trunk", "refs/heads/trunk", SyncOutcomeText.VerifyingDestination,
                    () => ArrivedAsync(f, "trunk"));
            case "force-push":
                await f.Commit(f.One, "outgoing"); await f.Git(f.One, "push", "origin", "trunk");
                await f.Git(f.One, "commit", "--amend", "-m", "outgoing, reworded");
                return new(Sync(f, SyncAction.ForcePush), "receive-pack", "refs/remotes/origin/trunk", "refs/heads/trunk", SyncOutcomeText.VerifyingDestination,
                    () => ArrivedAsync(f, "trunk"));
            case "publish":
                await f.Git(f.One, "switch", "-c", "topic"); await f.Commit(f.One, "topic");
                return new(Request(f, snapshot => new PublishRequest(snapshot, new(PublishKind.ExistingRemote, "origin", "topic", true))), "receive-pack",
                    "refs/remotes/origin/topic", "refs/heads/topic", SyncOutcomeText.VerifyingDestination, () => ArrivedAsync(f, "topic"));
            case "remote-branch-delete":
                // A clone packs every ref, so the deleted tracking ref is a packed one, and its deletion locks packed-refs.
                await f.Git(f.Two, "push", "origin", "trunk:refs/heads/gone"); await f.Git(f.One, "fetch", "origin");
                await f.Git(f.One, "pack-refs", "--all"); await f.Git(f.Bare, "pack-refs", "--all");
                return new(Request(f, snapshot => new RemoteBranchDeleteRequest(snapshot, "origin", "gone")), "receive-pack",
                    "refs/remotes/origin/gone", "refs/heads/gone", SyncOutcomeText.VerifyingRemote, async () =>
                    {
                        Assert.Equal("", await f.Git(f.Bare, "for-each-ref", "refs/heads/gone"));
                        Assert.Equal("", await f.Git(f.One, "for-each-ref", "refs/remotes/origin/gone"));
                    });
            case "remote-tag-push":
                await f.Git(f.One, "tag", "v1");
                return new(Request(f, snapshot => new RemoteTagRequest(snapshot, "origin", "v1", RemoteTagAction.Push)), "receive-pack",
                    RemoteRef: "refs/tags/v1", Verifying: SyncOutcomeText.VerifyingRemote,
                    Done: async () => Assert.Equal(await f.Git(f.One, "rev-parse", "v1"), await f.Git(f.Bare, "rev-parse", "v1")));
            case "remote-tag-delete":
                await f.Git(f.One, "tag", "v1"); await f.Git(f.One, "push", "origin", "refs/tags/v1");
                return new(Request(f, snapshot => new RemoteTagRequest(snapshot, "origin", "v1", RemoteTagAction.Delete)), "receive-pack",
                    RemoteRef: "refs/tags/v1", Verifying: SyncOutcomeText.VerifyingRemote,
                    Done: async () => Assert.Equal("", await f.Git(f.Bare, "for-each-ref", "refs/tags")));
            case "pull-lfs":
                await f.PushLfsFileAsync(); await f.Lfs(f.One, installed: true);
                await f.Git(f.One, "config", "lfs.url", silent.Url("lfs"));
                return new(Sync(f, SyncAction.Pull), "silent");
            case "push-lfs":
                await f.CommitLfsFileAsync();
                await f.Git(f.One, "config", "lfs.url", silent.Url("lfs"));
                return new(Sync(f, SyncAction.Push), "silent");
            case "lfs-download":
                return new(Request(f, snapshot => new LfsDownloadRequest(snapshot, "origin", snapshot.Head!)), "silent");
            case "submodule-update":
                // A submodule One's index records and nobody cloned yet, whose server never answers.
                await f.Git(f.One, "config", "--file", ".gitmodules", "submodule.sub.path", "sub");
                await f.Git(f.One, "config", "--file", ".gitmodules", "submodule.sub.url", silent.Url("sub.git"));
                await f.Git(f.One, "update-index", "--add", "--cacheinfo", "160000," + await f.Head(f.One) + ",sub");
                await f.Git(f.One, "add", ".gitmodules"); await f.Git(f.One, "commit", "-m", "sub");
                return new(Request(f, snapshot => new SubmoduleUpdateRequest(snapshot)), "silent");
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    // The branch the remote received, and the remote-tracking ref that follows it.
    private static async Task ArrivedAsync(SyncFixture f, string branch)
    {
        var head = await f.Head(f.One);
        Assert.Equal(head, await f.Git(f.Bare, "rev-parse", "refs/heads/" + branch));
        Assert.Equal(head, await f.Git(f.One, "rev-parse", "refs/remotes/origin/" + branch));
    }

    private static Func<CancellationToken, IProgress<string>?, Task<GitWriteResult>> Sync(SyncFixture f, SyncAction action) => async (ct, progress) =>
    {
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        return await f.Sync.ExecuteAsync(new SyncRequest(snapshot, await f.Sync.ReadAsync(snapshot, "origin", null, f.Token), action) { Progress = progress }, ct);
    };

    private static Func<CancellationToken, IProgress<string>?, Task<GitWriteResult>> Request(SyncFixture f, Func<RepositorySnapshot, GitWriteRequest> request) =>
        async (ct, progress) => await f.Sync.ExecuteAsync(request(await f.Reader.ReadAsync(f.One, f.Token)) with { Progress = progress }, ct);

    // Runs the operation and stops it at the point: Cancel before it starts, once the remote waits, while Git holds the
    // locks of the refs it updates in One or in the bare remote (a reference-transaction hook keeps its prepared state,
    // as a slow disk would), or once the verification begins. At local-refs-quiet nobody cancels: Git holds One's locks
    // without a word until the quiet limit of the fixture's synchronizer stops it. A scenario that ends before it got
    // there fails, as it stopped nothing. Under a loaded suite Git may take long to get there, so only the fixture's own
    // limit bounds the wait.
    public async Task<GitWriteResult> StopAsync(SyncFixture f, string point, SilentServer silent)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        if (point == "before-start")
        {
            await cancel.CancelAsync();
            return await Run(cancel.Token, null);
        }
        if (point == "verifying")
        {
            var verifying = false;
            var result = await Run(cancel.Token, new OnReport(text => { if (text == Verifying) { verifying = true; cancel.Cancel(); } }));
            Assert.True(verifying, "The verification never began: " + result.Message);
            return result;
        }
        var reached = await StopPointAsync(f, point, silent);
        var pending = Run(cancel.Token, null);
        while (!reached() && !pending.IsCompleted) await Task.Delay(20, f.Token);
        Assert.True(reached(), "The stop point was not reached: " + (pending.IsCompletedSuccessfully ? pending.Result.Message : ""));
        if (point != "local-refs-quiet") await cancel.CancelAsync();
        return await pending;
    }

    private async Task<Func<bool>> StopPointAsync(SyncFixture f, string point, SilentServer silent)
    {
        var marker = Path.Combine(f.Root, point);
        switch (point == "remote-waits" ? Remote : point)
        {
            case "silent":
                return () => silent.Connected.IsCompleted;
            case "upload-pack":
                var script = Path.Combine(f.Root, "slow-upload.sh");
                await File.WriteAllTextAsync(script, "#!/bin/sh\nsleep 30 & echo ready > " + Quote(marker) + "\nwait\nexec git-upload-pack \"$@\"\n",
                    new UTF8Encoding(false), f.Token);
                await f.Git(f.One, "config", "remote.origin.uploadpack", "sh " + Quote(script));
                break;
            case "receive-pack":
                await f.Hook(f.Bare, "pre-receive", "sleep 30 & echo ready > " + Quote(marker) + "\nwait\n");
                break;
            case "local-refs-locked" or "local-refs-quiet":
                await f.Hook(f.One, "reference-transaction", Holding(LocalRef!, marker));
                break;
            case "remote-refs-locked":
                await f.Hook(f.Bare, "reference-transaction", Holding(RemoteRef!, marker));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(point), point, null);
        }
        return () => File.Exists(marker);
    }

    // Git holds the locks of the refs a transaction updates while the hook answers for its prepared state; only the
    // first transaction that updates the ref is held, so later updates, Trag's own among them, pass.
    internal static string Holding(string reference, string marker) => "[ \"$1\" = prepared ] || exit 0\ncase \"$(cat)\" in *\" " + reference
        + "\"*) ;; *) exit 0 ;; esac\n[ -e " + Quote(marker) + " ] && exit 0\nsleep 30 & echo ready > " + Quote(marker) + "\nwait\n";

    private sealed class OnReport(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
