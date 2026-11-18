using System.Text;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.Infrastructure.Git.Write;

namespace Trag.IntegrationTests.Git.Write;

public sealed class RepositoryWriteTests
{
    [Fact]
    public async Task WhenFilesAreStagedAndUnstagedBeforeTheFirstCommit_ShouldMoveOnlyTheSelectedWholeFiles()
    {
        using var f = await GitFixture.CreateAsync();
        await Put(f, "one.txt", "one\n"); await Put(f, "two.txt", "two\n");
        Assert.Equal(GitWriteOutcome.Succeeded, (await Stage(f, "one.txt")).Outcome);
        var snapshot = await f.ReadAsync();
        Assert.True(snapshot.Files.Single(file => file.Path.Display == "one.txt").IsStaged);
        Assert.False(snapshot.Files.Single(file => file.Path.Display == "two.txt").IsStaged);
        Assert.Equal(GitWriteOutcome.Succeeded, (await Unstage(f, "one.txt")).Outcome);
        Assert.All((await f.ReadAsync()).Files, file => Assert.Equal('?', file.Worktree));
        Assert.Equal("one\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "one.txt")));
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "index.lock")));
    }
    [Fact]
    public async Task WhenTheFirstAndAFollowingCommitAreMade_ShouldCommitOnlyTheIndexAndKeepPartialStaging()
    {
        using var f = await GitFixture.CreateAsync();
        await Put(f, "partial č.txt", "base\n"); await Put(f, "other.txt", "other\n");
        await f.GitAsync("add", "partial č.txt");
        var initial = await Commit(f, "Initial č commit\n\nMessage body");
        Assert.Equal(GitWriteOutcome.Succeeded, initial.Outcome);
        Assert.NotNull(initial.CommitId);
        Assert.Equal("base\n", await GitText(f, "show", "HEAD:partial č.txt"));
        await Put(f, "partial č.txt", "base\nstaged\n");
        await f.GitAsync("add", "partial č.txt");
        await Put(f, "partial č.txt", "base\nstaged\nunstaged\n");
        var committed = await Commit(f, "Commit staged content only");
        Assert.Equal(GitWriteOutcome.Succeeded, committed.Outcome);
        Assert.Equal(initial.CommitId, (await GitText(f, "rev-parse", "HEAD^")).Trim());
        Assert.Equal("base\nstaged\n", await GitText(f, "show", "HEAD:partial č.txt"));
        Assert.Equal("base\nstaged\nunstaged\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "partial č.txt")));
        Assert.Contains((await f.ReadAsync()).Files, file => file.Path.Display == "other.txt" && file.Worktree == '?');
        Assert.Equal(GitWriteOutcome.Succeeded, (await Stage(f, "partial č.txt")).Outcome);
        Assert.Equal("base\nstaged\nunstaged\n", await GitText(f, "show", ":partial č.txt"));
        Assert.False((await f.ReadAsync()).Files.Single(file => file.Path.Display == "other.txt").IsStaged);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(f.Root, ".git"), "trag-write-*"));
    }
    [Fact]
    public async Task WhenAGroupHoldsRenamesDeletionsAndOddNames_ShouldStageUnstageAndCommitEveryNameLiterally()
    {
        using var f = await GitFixture.CreateAsync();
        var special = OperatingSystem.IsWindows() ? "-space [x] č.txt" : "-space [x] č\t\n.txt";
        await Put(f, special, "special\n"); await Put(f, "old.txt", "old\n");
        Assert.Equal(GitWriteOutcome.Succeeded, (await Stage(f, special, "old.txt")).Outcome);
        Assert.Equal(GitWriteOutcome.Succeeded, (await Commit(f, "Initial")).Outcome);
        await f.GitAsync("mv", "old.txt", "renamed.txt");
        File.Delete(Path.Combine(f.Root, special));
        Assert.Equal(GitWriteOutcome.Succeeded, (await Stage(f, special)).Outcome);
        Assert.Equal(GitWriteOutcome.Succeeded, (await Unstage(f, "renamed.txt", "old.txt", special)).Outcome);
        Assert.False(File.Exists(Path.Combine(f.Root, special)));
        Assert.True(File.Exists(Path.Combine(f.Root, "renamed.txt")));
        Assert.All((await f.ReadAsync()).Files, file => Assert.False(file.IsStaged));
        Assert.Equal(GitWriteOutcome.Succeeded, (await Stage(f, "old.txt", "renamed.txt", special)).Outcome);
        Assert.Equal(GitWriteOutcome.Succeeded, (await Commit(f, "Commit rename and deletion")).Outcome);
        Assert.Equal("renamed.txt\0", await GitText(f, "ls-tree", "--name-only", "-z", "HEAD"));
    }
    // A rename already in the index has nothing left under its old name: the change made after it is staged under the
    // new name alone, which is what the file's StagePaths say; both names would make git add refuse the whole write.
    [Fact]
    public async Task WhenARenamedFileChangedAfterTheRenameWasStaged_ShouldStageTheChangeUnderItsNewNameAlone()
    {
        using var f = await GitFixture.CreateAsync();
        var lines = string.Concat(Enumerable.Range(1, 20).Select(i => "line " + i + "\n"));
        await Put(f, "old.txt", lines);
        Assert.Equal(GitWriteOutcome.Succeeded, (await Stage(f, "old.txt")).Outcome);
        Assert.Equal(GitWriteOutcome.Succeeded, (await Commit(f, "Initial")).Outcome);
        await f.GitAsync("mv", "old.txt", "renamed.txt");
        await Put(f, "renamed.txt", lines + "changed\n");
        var file = (await f.ReadAsync()).Files.Single();
        Assert.Equal(('R', 'M', "old.txt"), (file.Index, file.Worktree, file.OriginalPath!.Display));

        Assert.Equal(GitWriteOutcome.Succeeded, (await Stage(f, [.. file.StagePaths.Select(path => path.Display)])).Outcome);

        Assert.Equal(lines + "changed\n", await GitText(f, "show", ":renamed.txt"));
        var staged = (await f.ReadAsync()).Files.Single();
        Assert.True(staged.IsStaged); Assert.False(staged.IsUnstaged); Assert.Equal("old.txt", staged.OriginalPath!.Display);
    }
    [Fact]
    public async Task WhenAttributesNormalizeLineEndings_ShouldStageTheNormalizedContentAndLeaveTheWorkingFileAlone()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("config", "core.autocrlf", "false");
        await Put(f, ".gitattributes", "*.txt text eol=lf\n");
        await Put(f, "windows.txt", "one\r\ntwo\r\n");
        var result = await Stage(f, ".gitattributes", "windows.txt");
        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("one\ntwo\n", await GitText(f, "show", ":windows.txt"));
        Assert.Equal("one\r\ntwo\r\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "windows.txt")));
    }
    [Fact]
    public async Task WhenNewAttributesUseLfsWithoutGitLfs_ShouldRefuseToStageBeforeRunningTheCleanFilter()
    {
        using var f = await GitFixture.CreateAsync();
        await LfsConfig.SetAsync(f, f.Root, installed: false);
        await Put(f, ".gitattributes", "*.bin filter=lfs\n");
        await Put(f, "asset.bin", "fixture\n");
        var result = await Stage(f, ".gitattributes", "asset.bin");
        Assert.Equal((GitWriteOutcome.Rejected, IndexOutcomeText.LfsNotInstalled), (result.Outcome, result.Message));
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "index")));
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "index.lock")));
    }

    // With Git LFS installed, its filter stages the pointer and keeps the content in the repository's LFS storage.
    [Fact]
    public async Task WhenNewAttributesUseLfsWithGitLfs_ShouldStageThePointerAndStoreTheContent()
    {
        using var f = await GitFixture.CreateAsync();
        await LfsConfig.SetAsync(f, f.Root, installed: true);
        await Put(f, ".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        await Put(f, "asset.bin", "fixture\n");
        var oid = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("fixture\n"u8));

        var result = await Stage(f, ".gitattributes", "asset.bin");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("version https://git-lfs.github.com/spec/v1\noid sha256:" + oid + "\nsize 8\n", await GitText(f, "show", ":asset.bin"));
        Assert.True(File.Exists(Path.Combine(f.Root, ".git", "lfs", "objects", oid[..2], oid[2..4], oid)));
    }

    [Fact]
    public async Task WhenAHookStagesAnotherFileAndFails_ShouldKeepTheRealIndex()
    {
        using var f = await StagedFixture();
        var index = await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index"));
        await Hook(f, "pre-commit", "printf 'hook file\\n' > generated.txt\ngit add -- generated.txt\necho 'Fixture hook rejected this commit' >&2\nexit 1\n");
        var result = await Commit(f, "Keep this message");
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.Contains("Fixture hook rejected", result.Details);
        Assert.Equal(index, await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index")));
        Assert.Null((await f.ReadAsync()).Head);
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "index.lock")));
    }
    [Fact]
    public async Task WhenTheIdentityIsMissing_ShouldRefuseTheCommitAndKeepTheStagedContent()
    {
        using var f = await StagedFixture();
        await f.GitAsync("config", "user.useConfigOnly", "true");
        await f.GitAsync("config", "user.name", "");
        await f.GitAsync("config", "user.email", "");
        Assert.Null((await f.ReadAsync()).Author);
        Assert.Equal(GitWriteOutcome.Rejected, (await Commit(f, "Identity missing")).Outcome);
        Assert.Null((await f.ReadAsync()).Head);
        Assert.True(Assert.Single((await f.ReadAsync()).Files).IsStaged);
    }
    [Fact]
    public async Task WhenTheIndexIsLockedOrChangedSinceTheRead_ShouldRefuseTheCommitWithoutRemovingOrOverwritingEither()
    {
        using var f = await StagedFixture();
        var snapshot = await f.ReadAsync();
        var writer = new GitWriteRouter(new());
        var lockPath = Path.Combine(f.Root, ".git", "index.lock");
        await File.WriteAllTextAsync(lockPath, "owned by another process");
        var request = new CommitRequest(snapshot, "Blocked");
        Assert.Equal(GitWriteOutcome.Rejected, (await writer.ExecuteAsync(request, f.Token)).Outcome);
        Assert.Equal("owned by another process", await File.ReadAllTextAsync(lockPath));
        File.Delete(lockPath); // This test created this exact lock.
        await Put(f, "external.txt", "external\n");
        await f.GitAsync("add", "external.txt");
        var index = await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index"));
        Assert.Equal(GitWriteOutcome.Rejected, (await writer.ExecuteAsync(request, f.Token)).Outcome);
        Assert.Equal(index, await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index")));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAPostCommitHookWritesTheIndexOrMovesHead_ShouldKeepTheIndexLockedOrAskForAReview(bool changeHead)
    {
        using var f = await StagedFixture();
        var first = await Commit(f, "Initial");
        await Put(f, "one.txt", "next\n"); await f.GitAsync("add", "one.txt");
        var beforeIndex = await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index"));
        await Hook(f, "post-commit", changeHead
            ? "git update-ref HEAD " + first.CommitId + "\n"
            : "GIT_INDEX_FILE=\"$PWD/.git/index\" git add -- one.txt 2> .git/concurrent-error\nexit 0\n");
        var result = await Commit(f, "Concurrent fixture");
        if (changeHead)
        {
            Assert.Equal(GitWriteOutcome.Uncertain, result.Outcome);
            Assert.NotNull(result.CommitId);
            Assert.Equal(first.CommitId, (await f.ReadAsync()).Head);
            Assert.Equal(beforeIndex, await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index")));
            Assert.Single(Directory.EnumerateFiles(Path.Combine(f.Root, ".git"), "trag-write-*.index"));
        }
        else
        {
            Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
            Assert.Contains("index.lock", await File.ReadAllTextAsync(Path.Combine(f.Root, ".git", "concurrent-error")));
            Assert.Equal("next\n", await GitText(f, "show", "HEAD:one.txt"));
        }
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "index.lock")));
    }

    [Theory]
    [InlineData("pre-commit", GitWriteOutcome.Cancelled)]
    [InlineData("post-commit", GitWriteOutcome.Succeeded)]
    public async Task WhenACommitIsCancelledDuringAHook_ShouldCheckWhetherTheCommitAlreadyExists(string hook, GitWriteOutcome expected)
    {
        using var f = await StagedFixture();
        await Hook(f, hook, "sleep 30 & echo ready > .git/trag-hook-ready\nwait\n");
        using var cancellation = new CancellationTokenSource();
        var snapshot = await f.ReadAsync();
        var writer = new GitWriteRouter(new());
        var pending = writer.ExecuteAsync(new CommitRequest(snapshot, "Cancellation fixture"), cancellation.Token);
        while (!File.Exists(Path.Combine(f.Root, ".git", "trag-hook-ready")))
        {
            if (pending.IsCompleted) throw new InvalidOperationException((await pending).Message);
            await Task.Delay(30, f.Token);
        }
        cancellation.Cancel();
        var result = await pending;
        Assert.Equal(expected, result.Outcome);
        var after = await f.ReadAsync();
        if (expected == GitWriteOutcome.Succeeded) { Assert.Equal(result.CommitId, after.Head); Assert.Empty(after.Files); }
        else { Assert.Null(after.Head); Assert.True(Assert.Single(after.Files).IsStaged); }
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "index.lock")));
    }

    // A post-commit hook that leaves a job running, as one that reindexes the project does, holds Git's output after Git
    // is done; the commit is done with Git, not with the job.
    [Fact]
    public async Task WhenAPostCommitHookLeavesAJobRunning_ShouldFinishTheCommitWithGit()
    {
        using var f = await StagedFixture();
        await Hook(f, "post-commit", "sleep 30 &\n");
        var timer = System.Diagnostics.Stopwatch.StartNew();

        var result = await new GitWriteRouter(new()).ExecuteAsync(new CommitRequest(await f.ReadAsync(), "Background job"), f.Token);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), timer.Elapsed.ToString());
    }

    [Fact]
    public async Task WhenChangesAreDiscarded_ShouldRestoreTrackedFilesDeleteUntrackedOnesAndKeepTheIndex()
    {
        using var f = await GitFixture.CreateAsync();
        await Put(f, "one.txt", "v1\n"); await Put(f, "gone.txt", "keep me\n");
        await f.GitAsync("add", "."); await f.GitAsync("commit", "-m", "base");
        // one.txt: v2 staged, v3 in the working tree; gone.txt deleted on disk; scratch.txt untracked.
        await Put(f, "one.txt", "v2\n"); await f.GitAsync("add", "one.txt"); await Put(f, "one.txt", "v3\n");
        File.Delete(Path.Combine(f.Root, "gone.txt")); await Put(f, "scratch.txt", "temporary\n");
        var before = await f.ReadAsync();
        Assert.Equal(3, before.Files.Count);
        Assert.Equal(GitWriteOutcome.Rejected, (await Discard(f, "missing.txt")).Outcome);

        var result = await Discard(f, "one.txt", "gone.txt", "scratch.txt");
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details); Assert.Contains("3 files", result.Message); Assert.Contains("Untracked files were deleted", result.Message);
        Assert.Equal("v2\n", (await File.ReadAllTextAsync(Path.Combine(f.Root, "one.txt"), f.Token)).Replace("\r\n", "\n")); // autocrlf may check out CRLF
        Assert.Equal("keep me\n", (await File.ReadAllTextAsync(Path.Combine(f.Root, "gone.txt"), f.Token)).Replace("\r\n", "\n"));
        Assert.False(File.Exists(Path.Combine(f.Root, "scratch.txt")));
        var after = await f.ReadAsync();
        var one = Assert.Single(after.Files);
        Assert.Equal("one.txt", one.Path.Display); Assert.True(one.IsStaged); Assert.False(one.IsUnstaged);
        Assert.Contains("v2", await GitText(f, "show", ":one.txt"));
        // A staged-only file has nothing to discard, and validation says so instead of touching Git.
        Assert.Equal(GitWriteOutcome.Rejected, (await Discard(f, "one.txt")).Outcome);
    }
    // A lock a killed Git left behind (Cancel during a switch, Trag ended from Task Manager, a power cut) never goes away
    // by waiting. The refusal that meets it is the only thing the user reads, so it names the file to remove.
    [Fact]
    public async Task WhenALockNoProgramHoldsBlocksAWrite_ShouldNameTheLockFileInTheRefusal()
    {
        using var f = await GitFixture.CreateAsync();
        await Put(f, "one.txt", "v1\n");
        await f.GitAsync("add", "."); await f.GitAsync("commit", "-m", "base");
        await Put(f, "one.txt", "v2\n");
        await File.WriteAllBytesAsync(Path.Combine(f.Root, ".git", "index.lock"), [], f.Token);

        var staged = await Stage(f, "one.txt");

        Assert.Equal(GitWriteOutcome.Rejected, staged.Outcome);
        Assert.Contains("index.lock", staged.Message);
    }

    // A folder holding a Git repository of its own (a project cloned inside another, a submodule never registered) is
    // listed as one untracked entry, "inner/". git clean leaves such a folder alone without -ff and still exits 0, so
    // nothing may say it was deleted; like a submodule with changes of its own, the selection is refused whole.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAnUntrackedEntryIsARepositoryOfItsOwn_ShouldRefuseTheDiscardAndChangeNothing(bool withHistory)
    {
        using var f = await GitFixture.CreateAsync();
        await Put(f, "one.txt", "v1\n");
        await f.GitAsync("add", "."); await f.GitAsync("commit", "-m", "base");
        await Put(f, "one.txt", "v2\n");
        var inner = Path.Combine(f.Root, "inner");
        Directory.CreateDirectory(inner);
        await f.GitAtAsync(inner, "init", "-q");
        await File.WriteAllTextAsync(Path.Combine(inner, "work.txt"), "the other project's work\n", f.Token);
        if (withHistory)
        {
            await f.GitAtAsync(inner, "-c", "user.name=Other", "-c", "user.email=other@example.invalid", "add", ".");
            await f.GitAtAsync(inner, "-c", "user.name=Other", "-c", "user.email=other@example.invalid", "commit", "-q", "-m", "other");
        }
        Assert.Contains((await f.ReadAsync()).Files, file => file.Path.Display == "inner/" && file.IsUntracked);

        var alone = await Discard(f, "inner/");
        var together = await Discard(f, "inner/", "one.txt");

        Assert.Equal((GitWriteOutcome.Rejected, GitWriteOutcome.Rejected), (alone.Outcome, together.Outcome));
        Assert.True(File.Exists(Path.Combine(inner, "work.txt")));
        Assert.Equal("v2\n", (await File.ReadAllTextAsync(Path.Combine(f.Root, "one.txt"), f.Token)).Replace("\r\n", "\n"));
    }

    // A stage is one HEAD read, the same read after taking the lock and before publishing, and its two index commands;
    // an unstage has one index command. No status, no full read.
    [Fact]
    public async Task WhenAFileIsStagedAndUnstaged_ShouldReadOnlyTheStateEachWriteCompares()
    {
        using var f = await GitFixture.CreateAsync();
        await LfsConfig.SetAsync(f, f.Root, installed: true);
        await f.GitAsync("commit", "--allow-empty", "-m", "base");
        await Put(f, "one.txt", "one\n");
        var processes = 0;
        var writer = new GitWriteRouter(new(exited: _ => Interlocked.Increment(ref processes)));
        var path = new GitPath(Encoding.UTF8.GetBytes("one.txt"));
        Assert.Equal(GitWriteOutcome.Succeeded, (await writer.ExecuteAsync(new StageRequest(await f.ReadAsync(), [path]), f.Token)).Outcome);
        Assert.Equal(5, processes); // rev-parse, rev-parse, add, ls-files, rev-parse
        processes = 0;
        Assert.Equal(GitWriteOutcome.Succeeded, (await writer.ExecuteAsync(new UnstageRequest(await f.ReadAsync(), [path]), f.Token)).Outcome);
        Assert.Equal(4, processes); // rev-parse, rev-parse, restore, rev-parse
    }
    private static Task Put(GitFixture f, string path, string content) => File.WriteAllTextAsync(Path.Combine(f.Root, path), content, f.Token);
    private static async Task<string> GitText(GitFixture f, params string[] args)
    {
        var result = await f.RunAsync(args);
        Assert.Equal(0, result.ExitCode);
        return Encoding.UTF8.GetString(result.StandardOutput);
    }
    private static Task<GitWriteResult> Stage(GitFixture f, params string[] paths) => Write(f, (expected, selected) => new StageRequest(expected, selected), paths);
    private static Task<GitWriteResult> Unstage(GitFixture f, params string[] paths) => Write(f, (expected, selected) => new UnstageRequest(expected, selected), paths);
    private static Task<GitWriteResult> Discard(GitFixture f, params string[] paths) => Write(f, (expected, selected) => new DiscardRequest(expected, selected), paths);
    private static async Task<GitWriteResult> Write(GitFixture f, Func<RepositorySnapshot, GitPath[], GitWriteRequest> build, string[] paths) =>
        await new GitWriteRouter(new()).ExecuteAsync(
            build(await f.ReadAsync(), paths.Select(path => new GitPath(Encoding.UTF8.GetBytes(path))).ToArray()), f.Token);
    private static async Task<GitWriteResult> Commit(GitFixture f, string message) =>
        await new GitWriteRouter(new()).ExecuteAsync(new CommitRequest(await f.ReadAsync(), message), f.Token);
    private static async Task<GitFixture> StagedFixture()
    {
        var f = await GitFixture.CreateAsync();
        await Put(f, "one.txt", "one\n"); await f.GitAsync("add", "one.txt");
        return f;
    }
    private static async Task Hook(GitFixture f, string name, string body)
    {
        var directory = Directory.CreateDirectory(Path.Combine(f.Container, "empty-hooks")).FullName;
        var path = Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, "#!/bin/sh\n" + body, new UTF8Encoding(false), f.Token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
