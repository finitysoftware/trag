using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class FailuresTests
{
    [Fact]
    public void WhenAReadOrAWriteFailsAndARetryMayHelp_ShouldSaySoInOneSentenceWithTheSameAdvice()
    {
        Assert.Equal("The branch could not be created. Refresh and try again.", Failures.BranchNotCreated);
        Assert.Equal("Publishing could not start. Refresh and try again.", Failures.PublishNotStarted);
        Assert.Equal("The list could not be read. Refresh and try again.", Failures.ListNotRead);
        foreach (var sentence in new[] { Failures.BranchesNotRead, Failures.BranchNotDeleted, Failures.CommitNotCheckedOut, Failures.ChangesNotDiscarded,
            Failures.StashesNotRead, Failures.StashNotDropped, Failures.ChangesNotStashed })
            Assert.Matches(@"^[A-Z][a-z ]+ could not be [a-z ]+\. Refresh and try again\.$", sentence);
    }

    [Fact]
    public void WhenTheRepositoryChangedUnderAnOpenList_ShouldAdviseOpeningTheListAgain() =>
        Assert.Equal("Repository changed. Open the list again.", Failures.ListChanged);
}
