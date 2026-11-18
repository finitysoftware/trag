using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Status;

// A rebase stopped for an edit has made its commit already: a message the rebase view changed is one Continue gives that
// commit. Line breaks typed on Windows and the blank lines Git ends a message with change nothing.
public sealed class RepositoryRebaseTests
{
    private static readonly Trag.Domain.Status.RepositoryRebase Edit = Snapshots.Rebase() with { Message = "Commit 1\n\nBody\n", StoppedForEdit = true };

    [Fact]
    public void WhenAMessageIsWrittenAtAnEditStop_ShouldRewordOnlyWhenItIsAnotherOne()
    {
        Assert.True(Edit.Rewords("Commit 1, reworded"));
        Assert.False(Edit.Rewords("Commit 1\r\n\r\nBody"));
        Assert.False(Edit.Rewords("Commit 1\n\nBody\n\n"));
    }

    // At a conflict Git makes the commit itself, from the message Trag leaves for it.
    [Fact]
    public void WhenTheStopIsAConflict_ShouldNeverReword() =>
        Assert.False((Edit with { StoppedForEdit = false }).Rewords("Commit 1, reworded"));
}
