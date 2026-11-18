using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Write;

// Stashing the working tree against real Git when Cancel arrives at the worst moment.
public sealed class StashPushTests
{
    // The first Git a push runs reads refs/stash, the second stashes the changes. Cancel before the stash keeps the
    // changes in place; Cancel after Git stashed them cannot undo that, and the outcome says what Git did.
    [Theory]
    [InlineData(1, GitWriteOutcome.Cancelled, 0)]
    [InlineData(2, GitWriteOutcome.Succeeded, 1)]
    public async Task WhenCancelArrivesAfterAGitOfThePushExited_ShouldSayWhatHappenedToTheChanges(int exitedGits, GitWriteOutcome outcome, int stashes)
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "one\n");
        await fixture.CommitAsync("first");
        await fixture.WriteAsync("a.txt", "changed\n");
        using var cancellation = new CancellationTokenSource();
        var exits = 0;
        var git = new WriteCommandRunner(new GitProcessRunner(exited: _ =>
        {
            if (++exits == exitedGits) cancellation.Cancel();
        }));
        var writer = new StashWriter(git, new StashEntryResolver(git));
        var snapshot = await fixture.ReadAsync();

        var result = await writer.StashAsync(new StashRequest(snapshot, new(Message: "saved"), StashAction.Push), snapshot, cancellation.Token);

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(stashes, (await fixture.GitAsync("stash", "list")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    // A change shown in the list can be undone in an editor before the click, and an untracked file shown with it can be
    // gone, as an editor's lock file goes. Git then saves nothing, exits 0 and says there are no local changes to save:
    // the working tree is clean, and untracked files are no reason, left out or included.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenTheShownChangesAreUndoneBeforeAStash_ShouldSayThereIsNothingToStash(bool includeUntracked)
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "one\n");
        await fixture.CommitAsync("first");
        await fixture.WriteAsync("a.txt", "changed\n");
        if (includeUntracked) await fixture.WriteAsync("~$lock.docx", "lock\n");
        var shown = await fixture.ReadAsync();
        await fixture.WriteAsync("a.txt", "one\n");
        File.Delete(Path.Combine(fixture.Root, "~$lock.docx"));
        var git = new WriteCommandRunner(new GitProcessRunner());

        var result = await new StashWriter(git, new StashEntryResolver(git)).StashAsync(
            new StashRequest(shown, new(Message: "nothing left", IncludeUntracked: includeUntracked), StashAction.Push), shown, fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, StashOutcomeText.NothingToStash), (result.Outcome, result.Message));
        Assert.Equal("", await fixture.GitAsync("stash", "list"));
    }

    // "Include untracked files" is --include-untracked, not --all: an ignored file stays where it is, also in an untracked
    // folder whose other files go into the stash, and in a folder that holds nothing but ignored files.
    [Fact]
    public async Task WhenIncludeUntrackedMeetsIgnoredFiles_ShouldStashTheUntrackedOnesAndLeaveTheIgnoredInPlace()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync(".gitignore", "*.log\n");
        await fixture.CommitAsync("first");
        await fixture.WriteAsync("build/keep.txt", "keep\n");
        string[] ignored = ["build/out.log", "build/deep/x.log", "logs-only/y.log", "top.log"];
        foreach (var path in ignored) await fixture.WriteAsync(path, "ignored\n");
        var git = new WriteCommandRunner(new GitProcessRunner());
        var snapshot = await fixture.ReadAsync();

        var result = await new StashWriter(git, new StashEntryResolver(git)).StashAsync(
            new StashRequest(snapshot, new(Message: "untracked", IncludeUntracked: true), StashAction.Push), snapshot, fixture.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.All(ignored, path => Assert.True(File.Exists(Path.Combine(fixture.Root, path)), path));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "build", "keep.txt")));
        Assert.Equal("build/keep.txt", await fixture.GitAsync("ls-tree", "-r", "--name-only", "stash@{0}^3"));
    }

    // Git said it saved the changes, but another program took the new stash off the list before Trag looked for it: the
    // changes left the working tree with it, so the outcome is one to review, not a refusal.
    [Fact]
    public async Task WhenTheNewStashIsDroppedBeforeItIsVerified_ShouldSayNoNewStashAppeared()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "one\n");
        await fixture.CommitAsync("first");
        await fixture.WriteAsync("a.txt", "changed\n");
        var exits = 0;
        // The first Git a push runs reads refs/stash, the second stashes; the other program drops the stash right after.
        var git = new WriteCommandRunner(new GitProcessRunner(exited: _ =>
        {
            if (++exits == 2) fixture.GitAsync("stash", "drop").GetAwaiter().GetResult();
        }));
        var snapshot = await fixture.ReadAsync();

        var result = await new StashWriter(git, new StashEntryResolver(git)).StashAsync(new StashRequest(snapshot, new(Message: "saved"), StashAction.Push), snapshot, fixture.Token);

        Assert.Equal((GitWriteOutcome.Uncertain, StashOutcomeText.NoNewStash), (result.Outcome, result.Message));
    }
}
