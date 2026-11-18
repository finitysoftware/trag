using System.Collections.Concurrent;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.IntegrationTests.Journeys;

// An IDE saves a file every 100 ms while the user stages it whole, stages one hunk, commits and discards it in
// Trag. Every write Trag reports as done is checked against what Git holds afterwards, and no save of the IDE's is lost.
public sealed class IdeWriterJourneyTests
{
    private const string Name = "live.txt";
    private const string Typed = "line 5 typed in the IDE";

    [Fact]
    public async Task WhenAnIdeSavesTheFileEvery100MsWhileItIsStagedHunkStagedCommittedAndDiscarded_ShouldReportOnlyWhatGitHoldsAndLoseNoSave()
    {
        await using var journey = await JourneyShell.StartAsync();
        var shell = journey.Shell;
        var fixture = journey.Fixture;
        var root = fixture.Root;
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.WriteAsync(Name, Buffer(0, "line 5"));
        var start = await fixture.CommitAsync("Start", Name);
        journey.Host.Folder = root;
        await shell.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        await shell.Catalog.OpenSelectedRepositoryCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        // Every write's outcome, as the open repository's writer reports it.
        List<WriteOutcome> writes = [];
        var writer = shell.Repository.Writer;
        writer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(writer.LastWrite) && writer.LastWrite is { } write) lock (writes) writes.Add(write);
        };
        await using var ide = new Ide(Path.Combine(root, Name), fixture.Container);

        // Stage the whole file: Git holds one of the IDE's saves whole, never an older one than the diff showed.
        await ShellHarness.Until(() => shell.FileStatus.UnstagedFiles.Count == 1, "the watcher to show the IDE's save");
        var shown = await ShownTickAsync(shell);
        var staged = await RunAsync(shell, writes, shell.Commit.StageSelectedCommand, null);
        var index = await journey.Git(root, "show", ":" + Name) + "\n";
        Assert.True(staged.Result is { Outcome: GitWriteOutcome.Succeeded }, staged.Result?.Message + staged.Result?.Details);
        Assert.Contains(index, ide.Saved);
        Assert.True(Tick(index) >= shown, "staged tick " + Tick(index) + ", shown " + shown);

        // Stage one hunk the IDE typed: the index gets that line and nothing else of what the IDE saved since.
        ide.Line5 = Typed;
        await ShellHarness.Until(() => shell.FileStatus.UnstagedFiles.Count == 1, "the watcher to show the IDE's save beside the staged one");
        await SelectAsync(shell, () => shell.WorkingDiff.Diff.Document.Lines.Any(line => line.Text == "+" + Typed), "the diff to show the typed line");
        var lines = shell.WorkingDiff.Diff.Document.Lines;
        var block = DiffBlocks.At(lines, lines.ToList().FindIndex(line => line.Text == "+" + Typed))!.Value;
        var hunk = await RunAsync(shell, writes, shell.WorkingDiff.TakeHunkCommand, new HunkChoice(lines, block.Lines));
        Assert.True(hunk.Result is { Outcome: GitWriteOutcome.Succeeded }, hunk.Result?.Message + hunk.Result?.Details);
        var withHunk = index.Replace("\nline 5\n", "\n" + Typed + "\n", StringComparison.Ordinal);
        Assert.Equal(withHunk, await journey.Git(root, "show", ":" + Name) + "\n");

        // Commit: the commit holds exactly what was staged.
        shell.Commit.CommitMessage = "Commit while the IDE saves";
        var committed = await RunAsync(shell, writes, shell.Commit.CommitCommand, null);
        Assert.True(committed.Result is { Outcome: GitWriteOutcome.Succeeded }, committed.Result?.Message + committed.Result?.Details);
        Assert.Equal(start, await journey.Git(root, "rev-parse", "HEAD^"));
        Assert.Equal(withHunk, await journey.Git(root, "show", "HEAD:" + Name) + "\n");
        Assert.Equal("", await journey.Git(root, "diff", "--cached", "--name-only"));

        // Discard the file: Git puts back the commit's version, which the IDE finds on disk before its next save as the only
        // content it did not save itself, and the IDE's later saves are on disk again. Git deletes the file before it writes
        // that version, so a save the IDE moves in between makes Git refuse to create it: the user then finds the IDE's save
        // on disk and the index as committed, and discards again. A save that lands after Git put the version back and
        // before Trag reads the file's status again leaves the discard unverified: the IDE read Git's version before it
        // replaced it, so the discard took effect, and Trag said only what it could check. A save the IDE moved in after
        // Git wrote the file but before it read it again replaced Git's version unseen, and the user discards again.
        Assert.Empty(ide.Outside);
        List<WriteOutcome> raced = [];
        var discarded = await DiscardAsync(shell, writes);
        while (raced.Count < 3 && (discarded.Result is { Outcome: GitWriteOutcome.Rejected, Message: IndexOutcomeText.NotRestored } refusal
            && refusal.Details.Contains("unable to create file " + Name + ": File exists", StringComparison.Ordinal)
            || discarded.Result is { Outcome: GitWriteOutcome.Uncertain, Message: WriteText.NotVerified } && ide.Outside.Length == 0))
        {
            raced.Add(discarded);
            var found = await File.ReadAllTextAsync(Path.Combine(root, Name), fixture.Token);
            await ShellHarness.Until(() => ide.Saved.Contains(found), "the file the discard left to hold a save of the IDE's");
            Assert.Empty(ide.Outside);
            Assert.Equal(withHunk, await journey.Git(root, "show", ":" + Name) + "\n");
            discarded = await DiscardAsync(shell, writes);
        }
        if (discarded.Result is { Outcome: GitWriteOutcome.Uncertain, Message: WriteText.NotVerified }) raced.Add(discarded);
        else Assert.True(discarded.Result is { Outcome: GitWriteOutcome.Succeeded }, discarded.Result?.Message + discarded.Result?.Details);
        await ShellHarness.Until(() => ide.Outside.Length != 0, "the IDE to find the version Git put back");
        Assert.Equal(withHunk, Assert.Single(ide.Outside));
        var afterDiscard = ide.Count;
        await ShellHarness.Until(() => ide.Count >= afterDiscard + 3, "three more saves of the IDE");
        await ide.DisposeAsync();
        Assert.Equal(withHunk, Assert.Single(ide.Outside));
        var last = ide.Saved[^1];
        Assert.Equal(last, await File.ReadAllTextAsync(Path.Combine(root, Name), fixture.Token));
        Assert.Equal(withHunk, await journey.Git(root, "show", ":" + Name) + "\n");
        Assert.Equal("M " + Name, await fixture.GitAsync("--no-optional-locks", "status", "--porcelain"));
        await ShellHarness.Until(() => shell.FileStatus.UnstagedFiles.Count == 1 && !shell.Repository.IsBusy && shell.Repository.Snapshot!.Files.Count == 1, "the last save shown");
        Assert.All(writes.Except(raced), write => Assert.Equal(GitWriteOutcome.Succeeded, write.Result?.Outcome));
        await journey.Git(root, "fsck", "--no-progress");
    }

    // Forty lines, the fifth as the IDE's buffer holds it, and the save's number last.
    private static string Buffer(int tick, string line5) =>
        string.Concat(Enumerable.Range(1, 40).Select(number => (number == 5 ? line5 : "line " + number) + "\n")) + "tick " + tick + "\n";

    private static int Tick(string content) => int.Parse(content.Split('\n')[^2]["tick ".Length..], System.Globalization.CultureInfo.InvariantCulture);

    // The user selects the file and waits for what that shows. The harness's dispatcher runs an update's list edits on a
    // thread-pool thread, so a click that lands inside them is taken for the update's own and cleared with it, which the
    // window's one UI thread never lets happen; the user then clicks the row again.
    private static Task SelectAsync(ShellViewModel shell, Func<bool> shown, string what)
    {
        shell.FileStatus.SelectedUnstaged = (StatusRow)shell.FileStatus.UnstagedFiles[0]!;
        return ShellHarness.Until(() =>
        {
            if (shell.FileStatus.SelectedUnstaged is null && shell.FileStatus.UnstagedFiles.Count == 1)
                shell.FileStatus.SelectedUnstaged = (StatusRow)shell.FileStatus.UnstagedFiles[0]!;
            return shown();
        }, what);
    }

    // The user selects the file; the save its diff shows.
    private static async Task<int> ShownTickAsync(ShellViewModel shell)
    {
        await SelectAsync(shell, () => shell.WorkingDiff.Diff.Document.Lines.Any(line => line.Text.StartsWith("+tick ", StringComparison.Ordinal)), "the diff of the IDE's save");
        return int.Parse(shell.WorkingDiff.Diff.Document.Lines.Last(line => line.Text.StartsWith("+tick ", StringComparison.Ordinal)).Text["+tick ".Length..], System.Globalization.CultureInfo.InvariantCulture);
    }

    // The user selects the file the IDE keeps saving, chooses Discard and confirms it.
    private static async Task<WriteOutcome> DiscardAsync(ShellViewModel shell, List<WriteOutcome> writes)
    {
        await ShellHarness.Until(() => shell.FileStatus.UnstagedFiles.Count == 1, "the watcher to show the IDE's next save");
        await SelectAsync(shell, () => shell.Discard.DiscardSelectedCommand.CanExecute(null), "Discard to be offered");
        shell.Discard.DiscardSelectedCommand.Execute(null);
        return await RunAsync(shell, writes, shell.Discard.Confirmation.ConfirmCommand, null);
    }

    // The user clicks once the button is enabled, as live updates keep the repository busy now and then; a click the
    // harness's thread-pool dispatcher let a read overtake started no write, and is made again.
    private static async Task<WriteOutcome> RunAsync(ShellViewModel shell, List<WriteOutcome> writes, IAsyncRelayCommand command, object? parameter)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await ShellHarness.Until(() => command.CanExecute(parameter), "the command to be enabled");
            int before;
            lock (writes) before = writes.Count;
            await command.ExecuteAsync(parameter);
            await shell.Sync.SyncCompletion;
            lock (writes) if (writes.Count > before) return writes[^1];
        }
        throw new InvalidOperationException("The command never started a write.");
    }

    // The IDE: it saves its buffer every 100 ms as editors do, into a file beside it that then replaces the old one, and
    // tries again on the next save when Git holds the file open. Right before it replaces the file it reads it, and notes
    // what it finds there that differs from what it knows is on disk: content another writer put there.
    private sealed class Ide : IAsyncDisposable
    {
        private readonly CancellationTokenSource stop = new();
        private readonly Task saving;
        private readonly ConcurrentQueue<string> saved = new();
        private readonly ConcurrentQueue<string> outside = new();

        public Ide(string path, string container)
        {
            var temporary = Path.Combine(container, "ide-save.tmp");
            saving = Task.Run(async () =>
            {
                var onDisk = await File.ReadAllTextAsync(path);
                for (var tick = 1; !stop.IsCancellationRequested; tick++)
                {
                    var content = Buffer(tick, Line5);
                    try
                    {
                        await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false));
                        var found = await File.ReadAllTextAsync(path);
                        if (found != onDisk) outside.Enqueue(onDisk = found);
                        File.Move(temporary, path, overwrite: true);
                        onDisk = content;
                        saved.Enqueue(content);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    try { await Task.Delay(100, stop.Token); } catch (OperationCanceledException) { }
                }
            });
        }

        public string Line5 { get; set; } = "line 5";
        public string[] Saved => saved.ToArray();
        public int Count => saved.Count;
        public string[] Outside => outside.ToArray();

        public async ValueTask DisposeAsync()
        {
            if (!stop.IsCancellationRequested) await stop.CancelAsync();
            await saving;
        }
    }
}
