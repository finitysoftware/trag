using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.UnitTests.Presentation.Text;

// A revert and a cherry-pick end the same ways, each named its way in the sentence.
public sealed class PickTextTests
{
    [Fact]
    public void WhenAPickEndsWithoutANewCommit_ShouldNameItsKind()
    {
        Assert.Equal("Git could not cherry-pick the commit. Review the details below.", PickText.Failed(RepositoryOperation.CherryPick));
        Assert.Equal("Git could not revert the commit. Review the details below.", PickText.Failed(RepositoryOperation.Revert));
        Assert.Equal("The cherry-pick was interrupted before anything changed.", PickText.Interrupted(RepositoryOperation.CherryPick));
        Assert.Equal("The revert was interrupted before anything changed.", PickText.Interrupted(RepositoryOperation.Revert));
        Assert.Equal("The revert could not be verified. Review the current branch and working files before continuing.", PickText.NotVerified(RepositoryOperation.Revert));
    }
}
