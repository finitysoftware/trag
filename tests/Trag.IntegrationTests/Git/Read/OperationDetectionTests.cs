using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// Operations started in the terminal that Trag leaves to Git's tools: each is named in the header, refuses the writes
// that would mix into it, and its Continue and Abort, where Trag has them, run nothing; Git's state is checked in Git.
public sealed class OperationDetectionTests
{
    // git am stops on a patch that does not apply and leaves rebase-apply with its applying marker.
    [Fact]
    public async Task WhenAPatchApplicationStopsInTheTerminal_ShouldNameItAndRefuseToStage()
    {
        using var f = await DivergedAsync();
        var patch = Path.Combine(f.Container, "side.patch");
        await File.WriteAllTextAsync(patch, await f.GitAsync("format-patch", "-1", "--stdout", "side~1") + "\n", f.Token);
        Assert.NotEqual(0, (await f.RunAsync("am", patch)).ExitCode);
        await f.WriteAsync("new.txt", "new\n");
        var snapshot = await f.ReadAsync();

        var stage = await new GitWriteRouter(f.Runner).ExecuteAsync(new StageRequest(snapshot, [Assert.Single(snapshot.Files).Path]), f.Token);

        Assert.Equal(RepositoryOperation.ApplyMailbox, snapshot.Operation);
        Assert.StartsWith("Patch application in progress.", RepositoryHeader.From(snapshot).Warning, StringComparison.Ordinal);
        Assert.Equal((GitWriteOutcome.Rejected, WriteText.StateUnsupported), (stage.Outcome, stage.Message));
        Assert.Contains("?? new.txt", await f.GitAsync("status", "--porcelain"));
    }

    // A revert of three commits stopped at the second, and a cherry-pick of a range stopped at its second commit, are Git
    // sequences: Continue and Abort, which Trag has for a single pick, leave the branch and the sequence as Git left them.
    [Theory]
    [InlineData("revert")]
    [InlineData("cherry-pick")]
    public async Task WhenASequenceStopsPartWayInTheTerminal_ShouldNameItAndRefuseToContinueOrAbort(string command)
    {
        using var f = await DivergedAsync();
        if (command == "revert")
        {
            await f.GitAsync("switch", "-q", "side");
            await f.WriteAsync("conflict.txt", "later\n"); await f.CommitAsync("Later");
            Assert.NotEqual(0, (await f.RunAsync("revert", "--no-edit", "HEAD~1", "HEAD~2", "HEAD~3")).ExitCode);
        }
        else Assert.NotEqual(0, (await f.RunAsync("cherry-pick", "main..side")).ExitCode);
        var head = await f.GitAsync("rev-parse", "HEAD");
        var snapshot = await f.ReadAsync();
        var router = new GitWriteRouter(f.Runner);

        var continued = await router.ExecuteAsync(new PickContinueRequest(snapshot), f.Token);
        var aborted = await router.ExecuteAsync(new PickAbortRequest(snapshot), f.Token);

        Assert.Equal(RepositoryOperation.Sequencer, snapshot.Operation);
        Assert.StartsWith("Git sequence in progress.", RepositoryHeader.From(snapshot).Warning, StringComparison.Ordinal);
        Assert.Equal((GitWriteOutcome.Rejected, GitWriteOutcome.Rejected), (continued.Outcome, aborted.Outcome));
        Assert.Equal(head, await f.GitAsync("rev-parse", "HEAD"));
        Assert.True(Directory.Exists(Path.Combine(f.Root, ".git", "sequencer")));
        Assert.Contains("UU conflict.txt", await f.GitAsync("status", "--porcelain"));
    }

    // git merge --squash that conflicts starts no merge: the conflict is one Trag resolves, with no operation to finish.
    [Fact]
    public async Task WhenASquashMergeConflictsInTheTerminal_ShouldOfferItsConflictsWithoutAnOperationWarning()
    {
        using var f = await DivergedAsync();
        Assert.NotEqual(0, (await f.RunAsync("merge", "--squash", "side")).ExitCode);

        var snapshot = await f.ReadAsync();

        Assert.Equal(RepositoryOperation.Conflict, snapshot.Operation);
        Assert.True(snapshot.HasSupportedConflicts);
        Assert.Equal("", RepositoryHeader.From(snapshot).Warning);
        Assert.True(Assert.Single(snapshot.Files, file => file.Path.Display == "conflict.txt").IsConflict);
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "MERGE_HEAD")));
    }

    // main and side both change conflict.txt; side's three commits change one.txt, conflict.txt and three.txt in turn.
    private static async Task<GitFixture> DivergedAsync()
    {
        var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("conflict.txt", "base\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-q", "-c", "side");
        await f.WriteAsync("one.txt", "one\n"); await f.CommitAsync("One");
        await f.WriteAsync("conflict.txt", "side\n"); await f.CommitAsync("Side");
        await f.WriteAsync("three.txt", "three\n"); await f.CommitAsync("Three");
        await f.GitAsync("switch", "-q", "main");
        await f.WriteAsync("conflict.txt", "main\n"); await f.CommitAsync("Main");
        return f;
    }
}
