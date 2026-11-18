using System.Text;
using Trag.Domain.Diff;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// Staging, unstaging and a hunk through the private index when something else holds the index or Git itself: a lock
// another program holds, a second Trag on the same repository, a Git that is gone, and a selection too long for argv.
public sealed class StageTests
{
    [Fact]
    public async Task WhenTheIndexIsLockedByAnotherProcess_ShouldRefuseStageUnstageAndAHunkWithoutTouchingTheLock()
    {
        using var f = await ChangedAsync();
        var snapshot = await f.ReadAsync();
        var diff = await new CliFileDiffReader(new()).ReadAsync(f.Root, snapshot.Files.Single(file => file.Path.Display == "sample.txt"), false, false, f.Token);
        var changed = diff.Lines.ToList().FindIndex(line => line.Text == "+B");
        var hunk = HunkPatch.For(diff.Lines, DiffBlocks.At(diff.Lines, changed)!.Value.Lines, Sample, PatchAction.Stage);
        var lockPath = Path.Combine(f.Root, ".git", "index.lock");
        await File.WriteAllTextAsync(lockPath, "owned by another process", f.Token);
        var index = await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index"), f.Token);
        var router = new GitWriteRouter(new());

        foreach (GitWriteRequest request in new GitWriteRequest[] { new StageRequest(snapshot, [Sample]), new UnstageRequest(snapshot, [Staged]),
                     new PatchRequest(snapshot, Sample, hunk, PatchAction.Stage, Lines: false) })
        {
            var result = await router.ExecuteAsync(request, f.Token);
            Assert.Equal((GitWriteOutcome.Rejected, WriteText.IndexLockHeld), (result.Outcome, result.Message));
        }

        Assert.Equal("owned by another process", await File.ReadAllTextAsync(lockPath, f.Token));
        Assert.Equal(index, await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index"), f.Token));
        File.Delete(lockPath); // This test created this exact lock.
    }

    // Two Trag windows on one repository: the second write finds the first one's lock and is refused, and the first
    // finishes as if it were alone.
    [Fact]
    public async Task WhenTwoRoutersWriteTheSameRepositoryAtOnce_ShouldLetOneSucceedAndRejectTheOtherWithIndexLockHeld()
    {
        using var f = await ChangedAsync();
        await Hook(f, "pre-commit", "echo ready > .git/trag-hook-ready\nwhile [ ! -f .git/trag-hook-go ]; do sleep 0.1; done\n");
        var committing = new GitWriteRouter(new()).ExecuteAsync(new CommitRequest(await f.ReadAsync(), "First"), f.Token);
        while (!File.Exists(Path.Combine(f.Root, ".git", "trag-hook-ready")))
        {
            if (committing.IsCompleted) throw new InvalidOperationException((await committing).Message);
            await Task.Delay(30, f.Token);
        }

        var second = await new GitWriteRouter(new()).ExecuteAsync(new StageRequest(await f.ReadAsync(), [Sample]), f.Token);
        await File.WriteAllTextAsync(Path.Combine(f.Root, ".git", "trag-hook-go"), "", f.Token);
        var first = await committing;

        Assert.Equal((GitWriteOutcome.Rejected, WriteText.IndexLockHeld), (second.Outcome, second.Message));
        Assert.Equal(GitWriteOutcome.Succeeded, first.Outcome);
        Assert.Equal("staged\n", await f.GitAsync("show", "HEAD:staged.txt") + "\n");
        Assert.Equal("sample.txt", await f.GitAsync("diff", "--name-only"));
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "index.lock")));
    }

    // The Git Trag found when the repository opened was uninstalled or moved before the write.
    [Fact]
    public async Task WhenGitVanishesBetweenOpenAndWrite_ShouldRejectWithNotCompleted()
    {
        using var f = await ChangedAsync();
        var snapshot = await f.ReadAsync();
        var gone = new GitExecutable { Path = Path.Combine(f.Container, "uninstalled", OperatingSystem.IsWindows() ? "git.exe" : "git") };

        var result = await new GitWriteRouter(new(executable: gone)).ExecuteAsync(new StageRequest(snapshot, [Sample]), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, WriteText.NotCompleted), (result.Outcome, result.Message));
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "index.lock")));
        Assert.Equal("", await f.GitAsync("diff", "--cached", "--name-only", "--", "sample.txt"));
    }

    // Ten thousand long names are far beyond what a command line holds, so they reach Git on its standard input. Writing
    // and staging them takes most of a minute here and over two on a CI runner beside the rest of the suite.
    [Fact]
    public async Task WhenThePathSelectionIsTenThousandFiles_ShouldStageThemInOneGitCall()
    {
        using var f = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(5));
        await LfsConfig.SetAsync(f, f.Root, installed: true);
        await f.GitAsync("commit", "--allow-empty", "-m", "base");
        var names = Enumerable.Range(0, 10_000).Select(i => "files/" + i.ToString("D5") + " a name long enough to fill a command line.txt").ToArray();
        Directory.CreateDirectory(Path.Combine(f.Root, "files"));
        foreach (var name in names) await File.WriteAllTextAsync(Path.Combine(f.Root, name), name, f.Token);
        var processes = 0;
        var router = new GitWriteRouter(new(exited: _ => Interlocked.Increment(ref processes)));

        var result = await router.ExecuteAsync(new StageRequest(await f.ReadAsync(), names.Select(name => new GitPath(Encoding.UTF8.GetBytes(name))).ToArray()), f.Token);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal(5, processes); // rev-parse, rev-parse, add, ls-files, rev-parse
        Assert.Equal(10_000, (await f.GitAsync("diff", "--cached", "--name-only")).Split('\n').Length);
    }

    // core.safecrlf refuses a file whose mixed line endings would not survive the conversion: Git's own reason is shown.
    [Fact]
    public async Task WhenSafeCrlfRefusesAMixedLineEndingFile_ShouldReportNotUpdatedWithGitsReason()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("config", "core.autocrlf", "true");
        await f.GitAsync("config", "core.safecrlf", "true");
        await f.GitAsync("commit", "--allow-empty", "-m", "Base");
        await File.WriteAllTextAsync(Path.Combine(f.Root, "mixed.txt"), "one\r\ntwo\nthree\r\n", f.Token);

        var result = await new GitWriteRouter(new()).ExecuteAsync(new StageRequest(await f.ReadAsync(), [Named("mixed.txt")]), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, IndexOutcomeText.NotUpdated), (result.Outcome, result.Message));
        Assert.Contains("CRLF", result.Details);
        Assert.Equal("", await f.GitAsync("diff", "--cached", "--name-only"));
    }

    // A file deleted in the working tree: its deletion is staged, and unstaged again, by the paths its row carries.
    [Fact]
    public async Task WhenAFileIsDeleted_ShouldStageTheDeletionAndUnstageItBack()
    {
        using var f = await ChangedAsync();
        File.Delete(Path.Combine(f.Root, "sample.txt"));
        var router = new GitWriteRouter(new());

        var deleted = (await f.ReadAsync()).Files.Single(file => file.Path.Display == "sample.txt");
        var staged = await router.ExecuteAsync(new StageRequest(await f.ReadAsync(), [.. deleted.StagePaths]), f.Token);
        var row = (await f.ReadAsync()).Files.Single(file => file.Path.Display == "sample.txt");
        var unstaged = await router.ExecuteAsync(new UnstageRequest(await f.ReadAsync(), [.. row.Paths]), f.Token);
        var after = (await f.ReadAsync()).Files.Single(file => file.Path.Display == "sample.txt");

        Assert.Equal((GitWriteOutcome.Succeeded, GitWriteOutcome.Succeeded), (staged.Outcome, unstaged.Outcome));
        Assert.Equal(('D', '.'), (row.Index, row.Worktree));
        Assert.Equal(('.', 'D'), (after.Index, after.Worktree));
    }

    // Without Git LFS, a path the working tree's attributes give to LFS is refused only where its content would be
    // staged: a deletion, alone or as the old name of a rename, runs no clean filter. The index's own attributes do not
    // name LFS, as they would make the repository read-only before any stage.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenTheDeletionOrRenameOfAnLfsPathIsStagedWithoutGitLfs_ShouldStageIt(bool renamed)
    {
        using var f = await GitFixture.CreateAsync();
        await LfsConfig.SetAsync(f, f.Root, installed: false);
        await f.WriteAsync("asset.bin", "content\n");
        await f.CommitAsync("Asset", "asset.bin");
        await f.WriteAsync(".gitattributes", "*.bin filter=lfs\n");
        File.Delete(Path.Combine(f.Root, "asset.bin"));
        if (renamed) await f.WriteAsync("asset.dat", "content\n");
        GitPath[] paths = renamed ? [Named("asset.bin"), Named("asset.dat")] : [Named("asset.bin")];

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new StageRequest(await f.ReadAsync(), paths), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(renamed ? "R100\tasset.bin\tasset.dat" : "D\tasset.bin", await f.GitAsync("diff", "--cached", "--name-status", "-M"));
    }

    // A conflict whose other side deleted the file shows as deleted, yet our side's content stays on disk: staging its
    // resolution would run the clean filter, so without Git LFS it is refused like any other LFS file.
    [Fact]
    public async Task WhenAConflictDeletedByTheirsIsStagedWithLfsAttributesWithoutGitLfs_ShouldRefuse()
    {
        using var f = await GitFixture.CreateAsync("main");
        await LfsConfig.SetAsync(f, f.Root, installed: false);
        await f.WriteAsync("d.bin", "base\n");
        await f.CommitAsync("Base", "d.bin");
        await f.GitAsync("switch", "-c", "feature");
        await f.GitAsync("rm", "--quiet", "d.bin");
        await f.CommitAsync("Feature");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("d.bin", "main\n");
        await f.CommitAsync("Main", "d.bin");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
        await f.WriteAsync(".gitattributes", "*.bin filter=lfs\n");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new StageRequest(await f.ReadAsync(), [Named("d.bin")], Resolution: true), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, IndexOutcomeText.LfsNotInstalled), (result.Outcome, result.Message));
    }

    // A repository committed into the folder without .gitmodules, whose own checkout moved since: git add would stage
    // its new commit, but only a submodule .gitmodules lists is staged, and the refusal says so.
    [Fact]
    public async Task WhenAMovedGitlinkIsNotListedInGitmodules_ShouldSayWhyItIsNotStaged()
    {
        using var f = await GitFixture.CreateAsync();
        var inner = Directory.CreateDirectory(Path.Combine(f.Root, "inner")).FullName;
        await f.GitAtAsync(inner, "init", "--initial-branch=main");
        await SubmoduleFixture.IdentifyAsync(f, inner);
        await f.GitAtAsync(inner, "commit", "--allow-empty", "-m", "Inner one");
        var first = await f.GitAtAsync(inner, "rev-parse", "HEAD");
        await f.GitAsync("-c", "advice.addEmbeddedRepo=false", "add", "inner");
        await f.GitAsync("commit", "-m", "Embedded");
        await f.GitAtAsync(inner, "commit", "--allow-empty", "-m", "Inner two");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new StageRequest(await f.ReadAsync(), [Named("inner")]), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, IndexOutcomeText.SubmodulesNotStaged), (result.Outcome, result.Message));
        Assert.StartsWith("160000 " + first + " ", await f.GitAsync("ls-files", "--stage", "--", "inner"));
    }

    // A staged rename unstaged by its row's paths gives the old name back to the index.
    [Fact]
    public async Task WhenAStagedRenameIsUnstaged_ShouldRestoreTheOldNameInTheIndex()
    {
        using var f = await ChangedAsync();
        await f.WriteAsync("sample.txt", "a\nb\nc\n");
        await f.GitAsync("mv", "sample.txt", "renamed.txt");
        var row = (await f.ReadAsync()).Files.Single(file => file.Path.Display == "renamed.txt");

        var result = await new GitWriteRouter(new()).ExecuteAsync(new UnstageRequest(await f.ReadAsync(), [.. row.Paths]), f.Token);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal('R', row.Index);
        Assert.Equal("sample.txt\nstaged.txt", await f.GitAsync("ls-files", "--cached"));
    }

    // git add -N records a new file's path without its content: Trag lists it as an added change of the working tree,
    // shows its whole content, and stages that content.
    [Fact]
    public async Task WhenAFileIsIntentToAdd_ShouldListItAsAddedDiffItWholeAndStageIt()
    {
        using var f = await ChangedAsync();
        await f.WriteAsync("new.txt", "new\nwork\n");
        await f.GitAsync("add", "-N", "new.txt");
        var row = (await f.ReadAsync()).Files.Single(file => file.Path.Display == "new.txt");
        var diff = await new CliFileDiffReader(new()).ReadAsync(f.Root, row, false, false, f.Token);

        var result = await new GitWriteRouter(new()).ExecuteAsync(new StageRequest(await f.ReadAsync(), [.. row.StagePaths]), f.Token);

        Assert.Equal(('.', 'A', true), (row.Index, row.Worktree, row.IsUnstaged));
        Assert.Equal(["+new", "+work"], diff.Lines.Where(line => line.IsChange).Select(line => line.Text));
        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("new\nwork", await f.GitAsync("show", ":new.txt"));
    }

    // Discarding such a file has nothing in the index to restore it to: it goes as a new file would, not emptied in place.
    [Fact]
    public async Task WhenAnIntentToAddFileIsDiscarded_ShouldDeleteItAsANewFile()
    {
        using var f = await ChangedAsync();
        await f.WriteAsync("new.txt", "new\nwork\n");
        await f.GitAsync("add", "-N", "new.txt");

        var result = await new GitWriteRouter(new()).ExecuteAsync(new DiscardRequest(await f.ReadAsync(), [Named("new.txt")]), f.Token);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.False(File.Exists(Path.Combine(f.Root, "new.txt")));
        Assert.DoesNotContain("new.txt", await f.GitAsync("ls-files", "--cached"));
    }

    // Staging and unstaging the file by its name changes the index for exactly that file; x.txt, changed beside it, is
    // what "[x].txt" would match as a glob.
    [Theory]
    [MemberData(nameof(HostileNameRows.Tracked), MemberType = typeof(HostileNameRows))]
    public async Task WhenAFileHasAHostileName_ShouldStageAndUnstageExactlyThatFile(string label)
    {
        var name = TestNames.Find(label).Path;
        using var f = await GitFixture.CreateAsync();
        await f.WriteAsync("x.txt", "decoy\n");
        await f.WriteAsync(name, "one\n");
        await f.CommitAsync("Both");
        await f.WriteAsync("x.txt", "decoy changed\n");
        await f.WriteAsync(name, "two\n");
        var router = new GitWriteRouter(f.Runner);

        var staged = await router.ExecuteAsync(new StageRequest(await f.ReadAsync(), [Named(name)]), f.Token);
        var inIndex = await StagedNamesAsync(f);
        var unstaged = await router.ExecuteAsync(new UnstageRequest(await f.ReadAsync(), [Named(name)]), f.Token);

        Assert.Equal((GitWriteOutcome.Succeeded, GitWriteOutcome.Succeeded), (staged.Outcome, unstaged.Outcome));
        Assert.Equal([name], inIndex);
        Assert.Empty(await StagedNamesAsync(f));
    }

    // Windows 11 writes a reserved device name with an extension as an ordinary file, which Git for Windows cannot index:
    // the stage is refused with Git's reason, which names the file, and the index stays as it was.
    [Theory]
    [MemberData(nameof(HostileNameRows.Unindexable), MemberType = typeof(HostileNameRows))]
    public async Task WhenAnUntrackedFileHasANameGitForWindowsCannotIndex_ShouldRefuseWithGitsReasonAndLeaveTheIndex(string label)
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = TestNames.Find(label).Path;
        using var f = await ChangedAsync();
        await f.WriteAsync(name, "hostile\n");
        var snapshot = await f.ReadAsync();
        var index = await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index"), f.Token);

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new StageRequest(snapshot, [Named(name)]), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, IndexOutcomeText.NotUpdated), (result.Outcome, result.Message));
        Assert.Contains("'" + name + "'", result.Details);
        Assert.Equal(index, await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index"), f.Token));
    }

    private static async Task<string[]> StagedNamesAsync(GitFixture f) =>
        Encoding.UTF8.GetString((await f.RunAsync("diff", "--cached", "--name-only", "-z")).StandardOutput).Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private static GitPath Named(string path) => new(Encoding.UTF8.GetBytes(path));

    private static readonly GitPath Sample = new("sample.txt"u8.ToArray());
    private static readonly GitPath Staged = new("staged.txt"u8.ToArray());

    // sample.txt committed and changed in the working tree, staged.txt new and staged.
    private static async Task<GitFixture> ChangedAsync()
    {
        var f = await GitFixture.CreateAsync();
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("sample.txt", "a\nb\nc\n");
        await f.CommitAsync("Before", "sample.txt");
        await f.WriteAsync("sample.txt", "a\nB\nc\n");
        await f.WriteAsync("staged.txt", "staged\n");
        await f.GitAsync("add", "staged.txt");
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
