using Trag.Domain.Presentation.Text;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Status;

// Trag does not write to a sparse checkout yet: the shell offers no write over its files and says why in the banner.
public sealed class SparseCheckoutTests
{
    [Fact]
    public async Task WhenFilesAreSelectedInASparseCheckout_ShouldOfferNoStageUnstageDiscardOrCommitAndSayWhyInTheBanner()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("in/a.txt"), Snapshots.Staged("top.txt")], limitations: [LimitationText.SparseCheckout]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");

        model.FileStatus.UnstagedSelection.Add((StatusRow)model.FileStatus.UnstagedFiles[0]);
        model.FileStatus.StagedSelection.Add((StatusRow)model.FileStatus.StagedFiles[0]);
        model.Commit.CommitMessage = "Commit in a cone";

        Assert.Equal((false, false, false, false), (model.Commit.StageSelectedCommand.CanExecute(null), model.Commit.UnstageSelectedCommand.CanExecute(null),
            model.Discard.DiscardSelectedCommand.CanExecute(null), model.Commit.CommitCommand.CanExecute(null)));
        Assert.Equal(LimitationText.SparseCheckout, model.StatusBar.RepositoryWarning);
    }

    // The picker still lists the branch; the switch is refused before any write reaches Git.
    [Fact]
    public async Task WhenABranchIsChosenInASparseCheckout_ShouldWriteNothing()
    {
        var h = new ShellHarness();
        h.Branches.Local.Add("other");
        h.Reader.Snapshot = Snapshots.Create(limitations: [LimitationText.SparseCheckout]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Branches.OpenBranchPickerCommand.Execute(null); await model.Branches.List.Completion;

        await model.Branches.SwitchToBranchCommand.ExecuteAsync(model.Branches.Visible.Local.Single(row => row.Name == "other"));

        Assert.Empty(h.Writer.Requests);
    }
}
