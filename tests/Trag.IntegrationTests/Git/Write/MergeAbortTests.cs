using static Trag.IntegrationTests.Git.Write.MergeScenario;
using Trag.Application.Workspace;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.IntegrationTests.Git.Sync;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// Aborting a merge restores the index and touches nothing else; when Git cannot restore it, the edits stay and the
// recovery index is reported instead of a reset.
public sealed class MergeAbortTests
{
    [Fact]
    public async Task WhenAMergeIsAborted_ShouldRestoreTheIndexAndKeepUnrelatedEditsAndUntrackedFiles()
    {
        using var f = await SyncFixture.Create();
        await File.WriteAllTextAsync(Path.Combine(f.One, "unrelated.txt"), "base\n", f.Token);
        await f.Git(f.One, "add", "unrelated.txt"); await f.Git(f.One, "commit", "-m", "base extra");
        await f.Git(f.One, "push", "origin", "HEAD"); await f.Git(f.Two, "pull", "--ff-only");
        await Diverge(f);
        var head = await f.Head(f.One);
        Assert.Equal(GitWriteOutcome.NeedsResolution, (await f.Execute(SyncAction.Pull)).Outcome);
        await File.WriteAllTextAsync(Path.Combine(f.One, "unrelated.txt"), "keep unrelated edit\n", f.Token);
        await File.WriteAllTextAsync(Path.Combine(f.One, "keep.txt"), "keep untracked\n", f.Token);
        var result = await AbortMerge(f);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(head, result.Snapshot!.Head); Assert.Equal(RepositoryOperation.None, result.Snapshot.Operation);
        Assert.Equal("keep unrelated edit\n", await File.ReadAllTextAsync(Path.Combine(f.One, "unrelated.txt"), f.Token));
        Assert.True(File.Exists(Path.Combine(f.One, "keep.txt")));
        Assert.Equal("", await f.Git(f.One, "diff", "--cached", "--name-only"));
    }

    [Fact]
    public async Task WhenGitCannotAbortOverAnEditedResolution_ShouldKeepTheEditAndReportTheRecoveryIndexWithoutAResetOrClean()
    {
        using var f = await Conflict();
        await File.WriteAllTextAsync(Path.Combine(f.One, "shared.txt"), "staged resolution\n", f.Token);
        await StageResolution(f, "shared.txt");
        await File.WriteAllTextAsync(Path.Combine(f.One, "shared.txt"), "keep later unstaged edit\n", f.Token);
        var before = await f.Reader.ReadAsync(f.One, f.Token);
        var result = await AbortMerge(f);
        Assert.Equal(GitWriteOutcome.Uncertain, result.Outcome); Assert.Equal(before.Head, result.Snapshot!.Head);
        Assert.Equal(before.IndexFingerprint, result.Snapshot.IndexFingerprint); Assert.Equal(RepositoryOperation.Merge, result.Snapshot.Operation);
        Assert.Equal("keep later unstaged edit\n", await File.ReadAllTextAsync(Path.Combine(f.One, "shared.txt"), f.Token));
        Assert.Contains(result.Snapshot.Limitations, text => text.Contains("recovery index", StringComparison.Ordinal));
    }

    // Git aborted, and then every Git that would verify it fails, as one cut off from the repository would: nothing says
    // the merge is gone, so the recovery index is kept and the outcome says the abort was not verified.
    [Fact]
    public async Task WhenAbortCannotBeVerified_ShouldKeepTheRecoveryIndexAndSayAbortNotVerified()
    {
        using var f = await Conflict();
        var aborted = Path.Combine(f.Root, "aborted");
        // cmd reads the script in its own code page and the fixture's folder name is not ASCII, so the mark is named by the
        // script's own folder.
        var git = await TestProgram.WriteAsync(f.Root, "verify-fails-git",
            "@if exist \"%~dp0aborted\" exit /b 128\r\n@\"" + new GitProcessRunner().Program + "\" %*\r\n"
            + "@set code=%ERRORLEVEL%\r\n@echo %* | findstr /C:\"merge --abort\" >nul && type nul > \"%~dp0aborted\"\r\n@exit /b %code%\r\n",
            "mark=\"$(dirname \"$0\")/aborted\"\n[ -e \"$mark\" ] && exit 128\n" + TestProgram.Quote(new GitProcessRunner().Program) + " \"$@\"\ncode=$?\n"
            + "case \"$*\" in *\"merge --abort\"*) : > \"$mark\" ;; esac\nexit $code\n", f.Token);
        await using var workspace = new RepositoryWorkspace(f.Reader, writer: new GitWriteRouter(new GitProcessRunner(executable: new() { Path = git })));
        var before = await workspace.OpenAsync(f.One, f.Token);

        var result = await workspace.WriteAsync(new MergeAbortRequest(before!), f.Token);

        Assert.True((GitWriteOutcome.Uncertain, MergeOutcomeText.AbortNotVerified) == (result.Outcome, result.Message), result.Message + result.Details);
        Assert.True(File.Exists(aborted));
        Assert.Contains((await f.Reader.ReadAsync(f.One, f.Token)).Limitations, text => text.Contains("recovery index", StringComparison.Ordinal));
    }

    // An editor that holds a conflicted file open without letting it be deleted keeps Git from replacing it: Git gives up
    // on that file, the abort is not complete, and the recovery index is kept with Git's reason. Only Windows lets a
    // program hold a file that way.
    [Fact]
    public async Task WhenAnEditorHoldsAConflictedFile_ShouldSayAbortIncomplete()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = await Conflict();
        var before = await f.Reader.ReadAsync(f.One, f.Token);
        GitWriteResult result;
        await using (new FileStream(Path.Combine(f.One, "shared.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            result = await AbortMerge(f);

        Assert.True((GitWriteOutcome.Uncertain, MergeOutcomeText.AbortIncomplete) == (result.Outcome, result.Message), result.Message + result.Details);
        Assert.Contains(result.Snapshot!.Limitations, text => text.Contains("recovery index", StringComparison.Ordinal));
        Assert.Equal(before.Head, result.Snapshot.Head);
    }
}
