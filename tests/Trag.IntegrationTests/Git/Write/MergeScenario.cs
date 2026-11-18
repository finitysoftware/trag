using System.Text;
using Trag.Application.Workspace;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.IntegrationTests.Git.Sync;
using Trag.Infrastructure.Git.Write;

namespace Trag.IntegrationTests.Git.Write;

// The steps the merge scenarios share: two clones made to diverge, the pull that leaves a conflict behind, and the
// merge writes issued through the real workspace the way the shell issues them.
internal static class MergeScenario
{
    public static async Task<SyncFixture> Conflict(bool delete = false)
    {
        var f = await SyncFixture.Create();
        try { await Diverge(f, delete); var result = await f.Execute(SyncAction.Pull); Assert.True(result.Outcome == GitWriteOutcome.NeedsResolution, result.Message + result.Details); return f; }
        catch { f.Dispose(); throw; }
    }

    public static async Task Diverge(SyncFixture f, bool delete = false)
    {
        if (delete) { await f.Git(f.One, "rm", "shared.txt"); await f.Git(f.One, "commit", "-m", "local deletion"); }
        else await f.Commit(f.One, "local edit");
        await f.Commit(f.Two, "remote edit"); await f.Git(f.Two, "push", "origin", "trunk");
    }

    // One commits a file of its own and Two pushes a change to shared.txt, so a pull merges them without a conflict.
    public static async Task DivergeCleanly(SyncFixture f)
    {
        await File.WriteAllTextAsync(Path.Combine(f.One, "local.txt"), "local\n", f.Token);
        await f.Git(f.One, "add", "local.txt"); await f.Git(f.One, "commit", "-m", "local change");
        await f.Commit(f.Two, "remote change"); await f.Git(f.Two, "push", "origin", "trunk");
    }

    public static Task<GitWriteResult> StageResolution(SyncFixture f, string path) => Write(f, before => new StageRequest(before, [new(Encoding.UTF8.GetBytes(path))], Resolution: true));
    public static Task<GitWriteResult> OpenEditor(SyncFixture f, string path) => Write(f, before => new MergeToolRequest(before, new(Encoding.UTF8.GetBytes(path)), Editor: true));
    public static Task<GitWriteResult> FinishMerge(SyncFixture f, string message, CancellationToken? ct = null) => Write(f, before => new CommitRequest(before, message, FinishesMerge: true), ct);
    public static Task<GitWriteResult> AbortMerge(SyncFixture f) => Write(f, before => new MergeAbortRequest(before));

    public static async Task<GitWriteResult> Write(SyncFixture f, Func<RepositorySnapshot, GitWriteRequest> build, CancellationToken? ct = null)
    {
        await using var workspace = new RepositoryWorkspace(f.Reader, writer: new GitWriteRouter(new()));
        var before = await workspace.OpenAsync(f.One, f.Token);
        return await workspace.WriteAsync(build(before!), ct ?? f.Token);
    }
}
