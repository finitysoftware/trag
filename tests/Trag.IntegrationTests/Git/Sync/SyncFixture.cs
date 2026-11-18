using System.Text;
using Trag.Domain.Sync;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Sync;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Sync;

// A bare origin with two clones of it inside one GitFixture container: One is the repository Trag works in, Two is the
// other side that pushes and pulls behind its back. Every sync, merge, branch and stash scenario over real remotes
// starts here. Its synchronizer stops a silent Git after the quiet limit given, or after the application's own.
internal sealed class SyncFixture(GitFixture owner, TimeSpan? quietLimit = null) : IDisposable
{
    public string Root => owner.Root;
    public string Bare => Path.Combine(Root, "origin.git");
    public string One => Path.Combine(Root, "one");
    public string Two => Path.Combine(Root, "two");
    public CancellationToken Token => owner.Token;
    public CliRepositoryReader Reader { get; } = new(owner.Runner);
    public GitSyncFacade Sync { get; } = new(owner.Runner, new CliRepositoryReader(owner.Runner), quietLimit);

    public static async Task<SyncFixture> Create(TimeSpan? quietLimit = null)
    {
        var f = new SyncFixture(await GitFixture.CreateAsync(), quietLimit);
        try
        {
            await f.Git(f.Root, "init", "--bare", "--initial-branch=trunk", f.Bare);
            await f.Git(f.Root, "-c", "core.autocrlf=false", "clone", f.Bare, f.One); await f.Configure(f.One);
            await f.Commit(f.One, "initial"); await f.Git(f.One, "push", "-u", "origin", "trunk");
            await f.Git(f.Root, "-c", "core.autocrlf=false", "clone", f.Bare, f.Two); await f.Configure(f.Two); return f;
        }
        catch { f.Dispose(); throw; }
    }

    private async Task Configure(string root)
    {
        await Git(root, "config", "user.name", "Sync Fixture"); await Git(root, "config", "user.email", "sync@example.invalid");
        await Git(root, "config", "commit.gpgsign", "false"); await Git(root, "config", "core.autocrlf", "false");
        await Git(root, "config", "core.hooksPath", Path.Combine(Root, "empty-hooks"));
    }

    public Task<string> Git(string root, params string[] args) => owner.GitAtAsync(root, args);
    public Task<string> Head(string root) => Git(root, "rev-parse", "HEAD");
    public Task Lfs(string root, bool installed) => LfsConfig.SetAsync(owner, root, installed);

    public async Task Commit(string root, string text)
    { await File.AppendAllTextAsync(Path.Combine(root, "shared.txt"), text + "\n", Token); await Git(root, "add", "shared.txt"); await Git(root, "commit", "-m", text); }

    // Two pushes a commit with an LFS file, its content first, or the commit alone: Git LFS puts its pre-push hook even into
    // the fixture's hooks folder, so the commit alone goes without hooks.
    public async Task PushLfsFileAsync(bool content = true, string branch = "trunk")
    {
        await Lfs(Two, installed: true);
        await File.WriteAllTextAsync(Path.Combine(Two, ".gitattributes"), "*.bin filter=lfs diff=lfs merge=lfs -text\n", Token);
        await File.WriteAllTextAsync(Path.Combine(Two, "asset.bin"), "fixture\n", Token);
        await Git(Two, "add", ".gitattributes", "asset.bin"); await Git(Two, "commit", "-m", "Asset");
        if (content) await Git(Two, "lfs", "push", "origin", branch);
        await Git(Two, "push", "--no-verify", "origin", branch);
    }

    // One commits an LFS file. A pre-push hook of One's own keeps Git LFS's hook out, so the content reaches the remote
    // only by Trag's push.
    public async Task CommitLfsFileAsync()
    {
        await Lfs(One, installed: true);
        await File.WriteAllTextAsync(Path.Combine(One, ".gitattributes"), "*.bin filter=lfs diff=lfs merge=lfs -text\n", Token);
        await File.WriteAllTextAsync(Path.Combine(One, "asset.bin"), "fixture\n", Token);
        await Git(One, "add", ".gitattributes", "asset.bin"); await Git(One, "commit", "-m", "Asset");
        await Hook(One, "pre-push", "exit 0\n");
    }

    public async Task<RepositorySyncState> State() => await Sync.ReadAsync(await Reader.ReadAsync(One, Token), "origin", null, Token);

    // Reads the status and the sync state the way the shell does before every action, then runs it.
    public async Task<GitWriteResult> Execute(SyncAction action, string remote = "origin", string? branch = null, CancellationToken? token = null,
        IProgress<string>? progress = null)
    {
        var ct = token ?? Token; var snapshot = await Reader.ReadAsync(One, ct);
        var state = await Sync.ReadAsync(snapshot, remote, branch, ct);
        return await Sync.ExecuteAsync(new SyncRequest(snapshot, state, action) { Progress = progress }, ct);
    }

    public async Task Hook(string root, string name, string body)
    {
        var hooks = Path.Combine(root, root == Bare ? "hooks" : ".git/hooks"); Directory.CreateDirectory(hooks);
        await Git(root, "config", "core.hooksPath", hooks);
        var path = Path.Combine(hooks, name); await File.WriteAllTextAsync(path, "#!/bin/sh\n" + body, new UTF8Encoding(false), Token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    // A path as a POSIX shell single-quoted word, for the sh scripts the fixtures write.
    public static string Quote(string path) => "'" + path.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    // A hook or script signals that it has started by creating a marker file; the test waits for the marker, not for time.
    // Under a loaded suite Git may take long to get there, so only the fixture's own limit bounds the wait.
    public static async Task WaitFor(string path, CancellationToken ct)
    {
        while (!File.Exists(path)) await Task.Delay(20, ct);
    }

    public void Dispose() => owner.Dispose();
}
