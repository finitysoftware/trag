using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Write;

// Dropping a stash against real Git while another program changes the stash list at the worst moment.
public sealed class StashDropTests
{
    // Git drops by index, so Trag finds the chosen entry's index first; a stash another program pushes in between moves
    // every entry down one, and the index then names the entry above the chosen one: one Trag listed, or the new one.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAnotherStashIsPushedBetweenResolvingAndDropping_ShouldPutBackTheEntryGitDroppedAndSaySo(bool chosenIsNewest)
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "one\n");
        await fixture.CommitAsync("first");
        var older = await StashAsync(fixture, chosenIsNewest ? "kept" : "chosen");
        var newest = await StashAsync(fixture, chosenIsNewest ? "chosen" : "kept");
        await fixture.WriteAsync("a.txt", "pushed\n");
        var exits = 0;
        // The first Git a drop runs lists the stashes; the other program pushes right after it.
        var git = new WriteCommandRunner(new GitProcessRunner(exited: _ =>
        {
            if (++exits == 1) fixture.GitAsync("stash", "push", "-m", "pushed").GetAwaiter().GetResult();
        }));
        var writer = new StashWriter(git, new StashEntryResolver(git));
        var snapshot = await fixture.ReadAsync();

        var result = await writer.StashAsync(new StashRequest(snapshot, new(Id: chosenIsNewest ? newest : older), StashAction.Drop), snapshot, fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, StashOutcomeText.ListChanged), (result.Outcome, result.Message));
        var left = (await fixture.GitAsync("stash", "list", "--format=%gs")).Split('\n');
        Assert.Equal(["On fixture: chosen", "On fixture: kept", "On fixture: pushed"], left.Order());
    }

    // The first Git a drop runs lists the stashes, the second drops the entry. Cancel before the drop keeps the stash; Cancel
    // after Git dropped it cannot undo that, and the outcome says what Git did.
    [Theory]
    [InlineData(1, GitWriteOutcome.Cancelled, 1)]
    [InlineData(2, GitWriteOutcome.Succeeded, 0)]
    public async Task WhenCancelArrivesAfterAGitOfTheDropExited_ShouldSayWhatHappenedToTheStash(int exitedGits, GitWriteOutcome outcome, int stashesLeft)
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "one\n");
        await fixture.CommitAsync("first");
        var chosen = await StashAsync(fixture, "chosen");
        using var cancellation = new CancellationTokenSource();
        var exits = 0;
        var git = new WriteCommandRunner(new GitProcessRunner(exited: _ =>
        {
            if (++exits == exitedGits) cancellation.Cancel();
        }));
        var writer = new StashWriter(git, new StashEntryResolver(git));
        var snapshot = await fixture.ReadAsync();

        var result = await writer.StashAsync(new StashRequest(snapshot, new(Id: chosen), StashAction.Drop), snapshot, cancellation.Token);

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(stashesLeft, (await fixture.GitAsync("stash", "list")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    private static async Task<string> StashAsync(GitFixture fixture, string message)
    {
        await fixture.WriteAsync("a.txt", message + "\n");
        await fixture.GitAsync("stash", "push", "-m", message);
        return await fixture.GitAsync("rev-parse", "refs/stash");
    }
}
