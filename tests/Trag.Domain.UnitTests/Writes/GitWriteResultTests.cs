using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Writes;

// A write Git did, or began and left at conflicts for the user, is under way: the question or the form that asked for it
// is done.
public sealed class GitWriteResultTests
{
    [Theory]
    [InlineData(GitWriteOutcome.Succeeded, true)]
    [InlineData(GitWriteOutcome.NeedsResolution, true)]
    [InlineData(GitWriteOutcome.Rejected, false)]
    [InlineData(GitWriteOutcome.Cancelled, false)]
    [InlineData(GitWriteOutcome.Uncertain, false)]
    public void WhenGitAnswersAWrite_ShouldSayWhetherItStarted(GitWriteOutcome outcome, bool started) =>
        Assert.Equal(started, new GitWriteResult(outcome, "").Started);

    private static readonly RepositorySnapshot Before = Snapshots.Create(files: [Snapshots.Modified("notes.txt")]);

    // Cancel stopped Git while it wrote the working tree (a switch halfway, a hard reset, a discard): Git answers as if
    // nothing happened, but the repository read afterwards is not the one the write began from.
    [Theory]
    [InlineData(GitWriteOutcome.Cancelled)]
    [InlineData(GitWriteOutcome.Rejected)]
    public void WhenAStoppedWriteLeftFilesChanged_ShouldAskForAReview(GitWriteOutcome outcome)
    {
        var after = Before with { Files = [Snapshots.Modified("notes.txt"), Snapshots.Modified("a.txt")] };

        var judged = new GitWriteResult(outcome, "Stopped", "details", Snapshot: after).AfterStop(Before);

        Assert.Equal((GitWriteOutcome.Uncertain, WriteText.StoppedPartway, "details"), (judged.Outcome, judged.Message, judged.Details));
        Assert.Same(after, judged.Snapshot);
    }

    [Fact]
    public void WhenAStoppedWriteMovedHead_ShouldAskForAReview()
    {
        var after = Before with { Head = Snapshots.Id('e') };

        Assert.Equal(GitWriteOutcome.Uncertain, new GitWriteResult(GitWriteOutcome.Cancelled, "Stopped", Snapshot: after).AfterStop(Before).Outcome);
    }

    // Stopped before Git changed anything, or ended any other way: what it said stands.
    [Theory]
    [InlineData(GitWriteOutcome.Cancelled, false)]
    [InlineData(GitWriteOutcome.Succeeded, true)]
    [InlineData(GitWriteOutcome.NeedsResolution, true)]
    public void WhenAStoppedWriteLeftTheRepositoryAsItWasOrEndedOtherwise_ShouldKeepItsOutcome(GitWriteOutcome outcome, bool changed)
    {
        var after = changed ? Before with { Files = [] } : Before with { Files = [Snapshots.Modified("notes.txt")] };
        var result = new GitWriteResult(outcome, "Said", Snapshot: after);

        Assert.Same(result, result.AfterStop(Before));
    }

    // A verification that could not read the repository already says so; there is nothing to compare.
    [Fact]
    public void WhenAStoppedWriteHasNoSnapshot_ShouldKeepItsOutcome()
    {
        var result = new GitWriteResult(GitWriteOutcome.Cancelled, "Said");

        Assert.Same(result, result.AfterStop(Before));
    }
}
