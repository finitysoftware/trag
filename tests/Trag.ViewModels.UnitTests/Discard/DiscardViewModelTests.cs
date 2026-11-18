using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Discard;

public sealed class DiscardViewModelTests
{
    // A submodule moved to another commit can be discarded, but a stash does not keep what a submodule has checked out,
    // so "stash instead" has nothing to keep: the question stays, with the reason, rather than closing on nothing.
    [Fact]
    public async Task WhenStashInsteadIsNotPossible_ShouldKeepTheQuestionAndSayWhy()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [new FileStatus(Snapshots.Path("sub"), '.', 'M', IsSubmodule: true, IsSubmoduleMoved: true)]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        model.Discard.DiscardSelectedCommand.Execute(null);

        model.Discard.StashInsteadOfDiscardCommand.Execute(null);

        Assert.True(model.Discard.Confirmation.IsOpen);
        Assert.Equal(StashOutcomeText.OnlySubmodules, model.Discard.Confirmation.Message);
        Assert.False(model.Stash.Draft.IsOpen); Assert.Empty(h.Writer.Requests);
    }
}
