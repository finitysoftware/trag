using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.Status;

public sealed class SubmoduleUpdateViewModelTests
{
    [Fact]
    public async Task WhenSubmodulesAreNotAtTheRecordedCommits_ShouldSaySoAndUpdateThem()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create() with { SubmodulesToUpdate = ["sub"] };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");

        Assert.Equal(SubmoduleText.NotAtRecorded(["sub"]), model.FileStatus.SubmoduleNotice);
        await model.FileStatus.UpdateSubmodulesCommand.ExecuteAsync(null);

        Assert.IsType<SubmoduleUpdateRequest>(Assert.Single(h.Writer.Requests));
    }

    [Fact]
    public async Task WhenEverySubmoduleIsAtItsRecordedCommit_ShouldSayNothingAndOfferNoUpdate()
    {
        var h = new ShellHarness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");

        Assert.Equal("", model.FileStatus.SubmoduleNotice);
        Assert.False(model.FileStatus.UpdateSubmodulesCommand.CanExecute(null));
    }
}
