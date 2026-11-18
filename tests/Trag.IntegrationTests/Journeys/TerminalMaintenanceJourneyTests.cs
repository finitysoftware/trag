using System.Text;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Journeys;

// A terminal runs git gc --prune=now and git maintenance run over and over while the user reads the history, a
// commit's diff and the blame of a file in Trag. Every read shows what Git holds, or says the repository changed.
public sealed class TerminalMaintenanceJourneyTests
{
    private const int Commits = 30;
    private const int Lines = 20;

    [Fact]
    public async Task WhenATerminalRunsGcAndMaintenanceDuringHistoryDiffAndBlameReads_ShouldReadOrSayTheRepositoryChanged()
    {
        await using var journey = await JourneyShell.StartAsync();
        var shell = journey.Shell;
        var fixture = journey.Fixture;
        var root = fixture.Root;
        var lines = Enumerable.Range(1, Lines).Select(number => "line " + number).ToArray();
        for (var commit = 1; commit <= Commits; commit++)
        {
            lines[commit % Lines] = "line " + (commit % Lines + 1) + " changed by commit " + commit;
            await fixture.WriteAsync("f.txt", string.Join("\n", lines) + "\n");
            await fixture.CommitAsync("Commit " + commit, "f.txt");
        }
        var head = await journey.Git(root, "rev-parse", "HEAD");

        journey.Host.Folder = root;
        await shell.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        await shell.Catalog.OpenSelectedRepositoryCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        List<string> errors = [];
        shell.StatusBar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(shell.StatusBar.ErrorMessage) && shell.StatusBar.ErrorMessage.Length != 0) lock (errors) errors.Add(shell.StatusBar.ErrorMessage);
        };

        // The terminal: unreachable objects to prune, then gc and maintenance, again and again.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        var runs = 0;
        var terminal = Task.Run(async () =>
        {
            for (var round = 0; !stop.IsCancellationRequested; round++)
            {
                await fixture.Runner.RunAsync(root, ["hash-object", "-w", "--stdin"], fixture.Token, standardInput: Encoding.UTF8.GetBytes("garbage " + round + "\n"));
                var maintained = await fixture.Runner.RunAsync(root, round % 2 == 0 ? ["gc", "--prune=now", "--quiet"] : ["maintenance", "run", "--quiet"], fixture.Token);
                if (maintained.ExitCode == 0) Interlocked.Increment(ref runs);
            }
        });

        // The user reads the history, a commit's diff and the blame, again and again while the terminal runs, at least until
        // the terminal finished six of its runs.
        List<string> unexpected = [];
        var reads = 0;
        while (reads < 20 || Volatile.Read(ref runs) < 6)
        {
            reads++;
            shell.Navigator.ShowHistoryCommand.Execute(null);
            await shell.History.HistoryCompletion;
            var page = shell.History.Page;
            if (page.Message == HistoryText.Changed) continue;
            if (page.Commits.Count != Commits || page.Commits[0].Id != head)
            {
                unexpected.Add("history: " + page.Message + " (" + page.Commits.Count + " commits)");
                continue;
            }

            var chosen = page.Commits[reads % Commits];
            shell.History.SelectedCommit = chosen;
            var details = shell.History.Details;
            await details.Completion;
            if (details.Panel.Details is null)
            {
                if (details.Panel.Heading != CommitText.Changed) unexpected.Add("commit: " + details.Panel.Heading);
                continue;
            }
            details.Panel.SelectedFile = Assert.Single(details.Panel.Files);
            await details.Diff.Completion;
            var diff = details.Diff.Document;
            if (!diff.Lines.Any(line => line.Text == "+" + Changed(chosen.Subject)) && diff.Message != DiffMessages.Commit.Changed) unexpected.Add("diff: " + diff.Message);

            await shell.Blame.OpenAsync("f.txt", null);
            await ShellHarness.Until(() => !shell.Blame.IsBusy, "the blame to be read");
            if (shell.Blame.Rows.Count != Lines && shell.Blame.Message != BlameText.Changed) unexpected.Add("blame: " + shell.Blame.Message + " (" + shell.Blame.Rows.Count + " rows)");
        }
        await stop.CancelAsync();
        try { await terminal; } catch (OperationCanceledException) { }

        Assert.Empty(unexpected);
        Assert.Empty(errors);
        Assert.Equal(head, await journey.Git(root, "rev-parse", "HEAD"));
        await journey.Git(root, "fsck", "--no-progress");
    }

    // The line the commit of that subject changed, as the diff adds it.
    private static string Changed(string subject)
    {
        var commit = int.Parse(subject["Commit ".Length..], System.Globalization.CultureInfo.InvariantCulture);
        return "line " + (commit % Lines + 1) + " changed by commit " + commit;
    }
}
