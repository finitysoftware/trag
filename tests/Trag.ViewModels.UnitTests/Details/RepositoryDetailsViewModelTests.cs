using Trag.Domain.Details;
using Trag.Domain.Presentation;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Details;

// The Repository details dialog: what the repository holds and who wrote its branch, read again each time it opens and
// dropped when it closes, its folder shown or copied through the window, and a person found in the search.
public sealed class RepositoryDetailsViewModelTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly RepositoryDetails Read = new(new(3, 5, Day, Day), [new(new("Ana", "ana@example.invalid"), 3)], RepositoryContents.None);

    private static async Task<(ShellViewModel Model, ShellHarness Harness)> OpenedAsync(string root = "fixture")
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Snapshots.Create(root: root); h.History.RepositoryDetails = Read;
        var model = h.Shell();
        await model.Session.OpenRepositoryAsync(root);
        return (model, h);
    }

    [Fact]
    public async Task WhenTheDialogOpens_ShouldReadTheDetailsAgainAndShowThem()
    {
        var (model, h) = await OpenedAsync();
        await using var _ = model;
        var details = model.RepositoryDetails;

        await details.OpenCommand.ExecuteAsync(null);
        details.CloseCommand.Execute(null);
        await details.OpenCommand.ExecuteAsync(null);

        Assert.True(details.IsOpen);
        Assert.Equal(2, h.History.DetailsReads);
        Assert.Equal("3 commits on main · 5 on all branches", details.Overview!.Commits);
        Assert.Equal(["Ana"], details.Overview.People.Select(person => person.Identity.Name));
    }

    // What was read goes with the dialog, and closing drops the read in flight: nothing it answers afterwards is shown.
    [Fact]
    public async Task WhenTheDialogClosesWhileReading_ShouldDropTheRead()
    {
        var (model, h) = await OpenedAsync();
        await using var _ = model;
        var details = model.RepositoryDetails;
        await details.OpenCommand.ExecuteAsync(null);
        details.CloseCommand.Execute(null);
        Assert.Null(details.Overview);
        h.History.Gate.IsClosed = true;
        var opening = details.OpenCommand.ExecuteAsync(null);
        await h.History.Gate.Started;
        Assert.True(details.Details.IsBusy); Assert.Null(details.Overview);

        details.CloseCommand.Execute(null);
        h.History.Gate.Open();
        await opening;

        Assert.False(details.IsOpen); Assert.False(details.Details.IsBusy); Assert.Null(details.Overview);
    }

    [Fact]
    public async Task WhenTheFolderIsShownOrCopied_ShouldHandTheRepositorysFolderToTheWindow()
    {
        var folder = Directory.CreateTempSubdirectory("trag-details-").FullName;
        try
        {
            var (model, _) = await OpenedAsync(folder);
            await using var _ = model;
            var host = new RecordingHost(); model.Host = host;

            model.RepositoryDetails.RevealCommand.Execute(null);
            model.RepositoryDetails.CopyPathCommand.Execute(null);

            Assert.Equal([folder], host.Revealed);
            Assert.Equal([("copy", folder)], host.Calls);
        }
        finally { Directory.Delete(folder); }
    }

    // A person is found by their address in the search, and the dialog makes way for it.
    [Fact]
    public async Task WhenAPersonIsChosen_ShouldSearchTheHistoryForTheirAddress()
    {
        var (model, _) = await OpenedAsync();
        await using var _ = model;
        var details = model.RepositoryDetails;
        await details.OpenCommand.ExecuteAsync(null);

        details.ShowPersonCommand.Execute(details.Overview!.People[0]);

        Assert.False(details.IsOpen);
        Assert.Equal((WorkspaceSection.Search, "ana@example.invalid"), (model.Navigator.Section, model.History.SearchQuery));
    }
}
