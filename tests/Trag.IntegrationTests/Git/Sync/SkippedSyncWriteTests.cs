using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Sync;

namespace Trag.IntegrationTests.Git.Sync;

// Every write the synchronizer carries out, run once by real Git, where it has to end done and leave what Done checks
// in One and in the bare remote, and once by a Git that skips each command that writes or pushes and exits 0 as if it
// had done it (Trag.GitStub with TRAG_GIT_STUB=push), with the commands that read, ls-remote among them, going to the
// real Git. The skipped run may report the write done only when it left what the real one did; otherwise it says
// that the remote or Git refused it or that it could not be verified, never that Cancel stopped it. The stub's mode is
// a variable of this process, so these tests run alone. A publish with push runs also with the stub's first mode, where
// only the writes are skipped and the push reaches the remote: the push names its refspec without -u, so only the
// configuration can say that the upstream was recorded. A pull fetches by real Git and has only its merge skipped. A
// fetch and a download of Git LFS content are left out: they trust Git's exit, since only another conversation with
// the remote could tell one that brought nothing from one Git skipped.
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class SkippedSyncWriteTests
{
    private static readonly string SilentGit = Path.Combine(AppContext.BaseDirectory, "Trag.GitStub" + (OperatingSystem.IsWindows() ? ".exe" : ""));

    [Theory]
    [InlineData("push", "push")]
    [InlineData("force-push", "push")]
    [InlineData("pull", "push")]
    [InlineData("publish", "push")]
    [InlineData("publish-new-remote", "push")]
    [InlineData("publish-tracking", "push")]
    [InlineData("publish-config-skipped", null)]
    [InlineData("remote-branch-delete", "push")]
    [InlineData("remote-tag-push", "push")]
    [InlineData("remote-tag-delete", "push")]
    [InlineData("submodule-update", "push")]
    public async Task WhenGitExitsZeroWithoutWritingOrPushing_ShouldNeverReportTheWriteDone(string write, string? stub)
    {
        using (var f = await SyncFixture.Create())
        {
            var (request, done) = await PrepareAsync(f, write);
            var result = await f.Sync.ExecuteAsync(await request(), f.Token);
            Assert.True(result.Outcome == GitWriteOutcome.Succeeded && await done(), write + " was not done by real Git: " + result.Outcome + " " + result.Message + " " + result.Details);
        }

        using (var f = await SyncFixture.Create())
        {
            var (request, done) = await PrepareAsync(f, write);
            var sync = new GitSyncFacade(new GitProcessRunner(executable: new() { Path = SilentGit }), f.Reader);
            GitWriteResult result;
            using (stub is null ? null : new EnvironmentVariables(("TRAG_GIT_STUB", stub))) result = await sync.ExecuteAsync(await request(), f.Token);

            Assert.NotEqual(GitWriteOutcome.Cancelled, result.Outcome);
            Assert.False(result.Outcome == GitWriteOutcome.Succeeded && !await done(), write + " reported done what Git skipped: " + result.Message);
            // Every publish but the one whose upstream was there before has its upstream left unwritten by the stub.
            if (write is "publish-new-remote" or "publish-tracking" or "publish-config-skipped")
                Assert.True(result.Outcome == GitWriteOutcome.Rejected && result.Message.EndsWith(PublishOutcomeText.UpstreamNotRecorded, StringComparison.Ordinal),
                    write + " did not say the upstream was not recorded: " + result.Outcome + " " + result.Message);
        }
    }

    // The request as the shell builds it from the repository it shows, and what the write leaves when Git did it.
    private static async Task<(Func<Task<GitWriteRequest>> Request, Func<Task<bool>> Done)> PrepareAsync(SyncFixture f, string write)
    {
        Func<Task<GitWriteRequest>> Sync(SyncAction action) => async () =>
        {
            var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
            return new SyncRequest(snapshot, await f.Sync.ReadAsync(snapshot, "origin", null, f.Token), action);
        };
        Func<Task<GitWriteRequest>> Shown(Func<RepositorySnapshot, GitWriteRequest> request) => async () => request(await f.Reader.ReadAsync(f.One, f.Token));

        switch (write)
        {
            case "push":
                await f.Commit(f.One, "outgoing");
                return (Sync(SyncAction.Push), () => ArrivedAsync(f, "trunk"));
            case "force-push":
                await f.Commit(f.One, "outgoing"); await f.Git(f.One, "push", "origin", "trunk");
                await f.Git(f.One, "commit", "--amend", "-m", "outgoing, reworded");
                return (Sync(SyncAction.ForcePush), () => ArrivedAsync(f, "trunk"));
            case "pull":
                await f.Commit(f.Two, "incoming"); await f.Git(f.Two, "push", "origin", "trunk");
                return (Sync(SyncAction.Pull), async () => await Ref(f, f.One, "HEAD") == await Ref(f, f.Bare, "refs/heads/trunk"));
            case "publish":
                // The upstream is already recorded, as a publish whose push failed leaves it, so the push is what decides.
                await f.Git(f.One, "switch", "-c", "topic"); await f.Commit(f.One, "topic");
                await f.Git(f.One, "config", "branch.topic.remote", "origin"); await f.Git(f.One, "config", "branch.topic.merge", "refs/heads/topic");
                return (Shown(snapshot => new PublishRequest(snapshot, new(PublishKind.ExistingRemote, "origin", "topic", true))),
                    async () => await ArrivedAsync(f, "topic") && await TracksAsync(f, "topic", "origin"));
            case "publish-tracking":
                await f.Git(f.One, "switch", "-c", "topic"); await f.Commit(f.One, "topic");
                return (Shown(snapshot => new PublishRequest(snapshot, new(PublishKind.ExistingRemote, "origin", "topic", false))), () => TracksAsync(f, "topic", "origin"));
            case "publish-config-skipped":
                await f.Git(f.One, "switch", "-c", "topic"); await f.Commit(f.One, "topic");
                return (Shown(snapshot => new PublishRequest(snapshot, new(PublishKind.ExistingRemote, "origin", "topic", true))),
                    async () => await ArrivedAsync(f, "topic") && await TracksAsync(f, "topic", "origin"));
            case "publish-new-remote":
                var mirror = new Uri(Path.Combine(f.Root, "mirror.git")).AbsoluteUri;
                await f.Git(f.Root, "init", "--bare", "--initial-branch=trunk", Path.Combine(f.Root, "mirror.git"));
                return (Shown(snapshot => new PublishRequest(snapshot, new(PublishKind.AddRemote, "mirror", "trunk", false, RemoteUrl: mirror))),
                    async () => await Config(f, "remote.mirror.url") == mirror && await TracksAsync(f, "trunk", "mirror"));
            case "remote-branch-delete":
                await f.Git(f.Two, "push", "origin", "trunk:refs/heads/gone"); await f.Git(f.One, "fetch", "origin");
                return (Shown(snapshot => new RemoteBranchDeleteRequest(snapshot, "origin", "gone")),
                    async () => await Ref(f, f.Bare, "refs/heads/gone") == "" && await Ref(f, f.One, "refs/remotes/origin/gone") == "");
            case "remote-tag-push":
                await f.Git(f.One, "tag", "v1");
                return (Shown(snapshot => new RemoteTagRequest(snapshot, "origin", "v1", RemoteTagAction.Push)),
                    async () => await Ref(f, f.Bare, "refs/tags/v1") == await Ref(f, f.One, "refs/tags/v1"));
            case "remote-tag-delete":
                await f.Git(f.One, "tag", "v1"); await f.Git(f.One, "push", "origin", "refs/tags/v1");
                return (Shown(snapshot => new RemoteTagRequest(snapshot, "origin", "v1", RemoteTagAction.Delete)), async () => await Ref(f, f.Bare, "refs/tags/v1") == "");
            default:
                // A submodule One records at its origin's tip and has checked out one commit back.
                var origin = Path.Combine(f.Root, "sub-origin");
                await f.Git(f.Root, "init", "--initial-branch=trunk", origin);
                await f.Git(origin, "config", "user.name", "Sync Fixture"); await f.Git(origin, "config", "user.email", "sync@example.invalid");
                await f.Git(origin, "commit", "--allow-empty", "-m", "first"); await f.Git(origin, "commit", "--allow-empty", "-m", "second");
                await f.Git(f.One, "-c", "protocol.file.allow=always", "submodule", "add", "--", new Uri(origin).AbsoluteUri, "sub"); await f.Git(f.One, "commit", "-m", "sub");
                var sub = Path.Combine(f.One, "sub");
                await f.Git(sub, "checkout", "--quiet", "HEAD~1");
                return (Shown(snapshot => new SubmoduleUpdateRequest(snapshot)),
                    async () => (await f.Git(f.One, "ls-tree", "HEAD", "sub")).Split(' ', '\t')[2] == await Ref(f, sub, "HEAD"));
        }
    }

    // The branch the remote received, and the remote-tracking ref that follows it.
    private static async Task<bool> ArrivedAsync(SyncFixture f, string branch)
    {
        var head = await Ref(f, f.One, "HEAD");
        return await Ref(f, f.Bare, "refs/heads/" + branch) == head && await Ref(f, f.One, "refs/remotes/origin/" + branch) == head;
    }

    private static async Task<bool> TracksAsync(SyncFixture f, string branch, string remote) =>
        await Config(f, "branch." + branch + ".remote") == remote && await Config(f, "branch." + branch + ".merge") == "refs/heads/" + branch;

    private static Task<string> Config(SyncFixture f, string key) => f.Git(f.One, "config", "--default", "", "--get", key);

    // The commit or tag a ref names, or "" without it.
    private static async Task<string> Ref(SyncFixture f, string root, string name) =>
        await f.Git(root, "for-each-ref", "--format=%(objectname)", name) is { Length: > 0 } id ? id : name == "HEAD" ? await f.Git(root, "rev-parse", "HEAD") : "";
}
