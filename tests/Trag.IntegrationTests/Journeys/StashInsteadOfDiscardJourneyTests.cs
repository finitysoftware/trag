using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Status;

namespace Trag.IntegrationTests.Journeys;

// A discard of the wrong file turned into a stash from the discard question, and the stash applied back, over real
// Git. The working tree comes back as it was; what was staged comes back unstaged, except a new file, since apply runs
// without --index (decided in 3.13.4), and the success message says so.
public sealed class StashInsteadOfDiscardJourneyTests
{
    [Fact]
    public async Task WhenTheDiscardQuestionStashesInsteadAndTheStashIsApplied_ShouldBringBackTheTreeWithTheStagedPartUnstaged()
    {
        await using var journey = await JourneyShell.StartAsync();
        var shell = journey.Shell;
        var fixture = journey.Fixture;
        var root = fixture.Root;
        await journey.Git(root, "checkout", "-q", "-b", "main");
        await journey.Git(root, "config", "core.autocrlf", "false");
        await fixture.WriteAsync("a.txt", "a1\na2\n");
        await fixture.WriteAsync("b.txt", "b1\n");
        var head = await fixture.CommitAsync("Base");
        await fixture.WriteAsync("a.txt", "a1 staged\na2\n");
        await fixture.WriteAsync("c.txt", "new and staged\n");
        await journey.Git(root, "add", "a.txt", "c.txt");
        await fixture.WriteAsync("a.txt", "a1 staged\na2 unstaged\n");
        await fixture.WriteAsync("b.txt", "b1 unstaged\n");
        await fixture.WriteAsync("d.txt", "untracked\n");
        var tree = Contents(root);
        var changes = await journey.Git(root, "diff", "HEAD");
        Assert.Equal("MM a.txt\n M b.txt\nA  c.txt\n?? d.txt", await journey.Git(root, "--no-optional-locks", "status", "--porcelain"));
        await shell.Session.OpenRepositoryAsync(root);
        await shell.Sync.SyncCompletion;

        // Discard b.txt: the question offers to stash everything instead, which opens the stash dialog in its place.
        shell.FileStatus.SelectedUnstaged = shell.FileStatus.UnstagedFiles.OfType<StatusRow>().Single(row => row.File.Path.Display == "b.txt");
        shell.Discard.DiscardSelectedCommand.Execute(null);
        Assert.True(shell.Discard.Confirmation.IsOpen);
        shell.Discard.StashInsteadOfDiscardCommand.Execute(null);
        Assert.False(shell.Discard.Confirmation.IsOpen);
        Assert.True(shell.Stash.Draft.IsOpen);
        await shell.Stash.CreateStashCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.False(shell.Stash.Draft.IsOpen, shell.Stash.Draft.Notice);
        Assert.Equal("?? d.txt", await journey.Git(root, "--no-optional-locks", "status", "--porcelain"));
        Assert.Equal("b1 unstaged\n", await journey.Git(root, "show", "stash@{0}:b.txt") + "\n");
        Assert.Equal("a1 staged\na2\n", await journey.Git(root, "show", "stash@{0}^2:a.txt") + "\n");
        Assert.Equal(head, await journey.Git(root, "rev-parse", "HEAD"));

        // Apply: every file is back as it was; a.txt's staged part is unstaged now, the new c.txt stays staged.
        await shell.Stash.OpenStashPickerCommand.ExecuteAsync(null);
        await shell.Stash.ApplyCommand.ExecuteAsync(Assert.Single(shell.Stash.Rows));
        await shell.Sync.SyncCompletion;
        Assert.Equal(StashOutcomeText.Applied("stash@{0}"), shell.StatusBar.Status);
        Assert.Equal(tree, Contents(root));
        Assert.Equal(changes, await journey.Git(root, "diff", "HEAD"));
        Assert.Equal("A\tc.txt", await journey.Git(root, "diff", "--cached", "--name-status"));
        Assert.Equal("M\ta.txt\nM\tb.txt", await journey.Git(root, "diff", "--name-status"));
        Assert.Equal("d.txt", await journey.Git(root, "ls-files", "--others", "--exclude-standard"));
        Assert.Equal("1", await journey.Git(root, "rev-list", "--walk-reflogs", "--count", "refs/stash"));
        await journey.Git(root, "fsck", "--no-progress");
    }

    private static Dictionary<string, string> Contents(string root) =>
        new[] { "a.txt", "b.txt", "c.txt", "d.txt" }.ToDictionary(name => name, name => File.ReadAllText(Path.Combine(root, name)));
}
