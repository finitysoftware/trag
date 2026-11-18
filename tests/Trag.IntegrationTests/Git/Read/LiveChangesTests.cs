using System.Text;
using Trag.TestSupport;
using Trag.Application.Workspace;
using Trag.Domain.Diff;
using Trag.Domain.Errors;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Platform.Watching;

namespace Trag.IntegrationTests.Git.Read;

public sealed class LiveChangesTests
{
    [Fact]
    public async Task WhenAFileWithAnUnusualNameIsAddedThenPartlyStaged_ShouldDiffEachSideFromItsOwnSourceByTheLiteralPathWithoutWritingTheIndex()
    {
        using var fixture = await GitFixture.CreateAsync();
        var path = OperatingSystem.IsWindows() ? "-space č [x].txt" : "-space č [x]\t\n.txt";
        var full = Path.Combine(fixture.Root, path);
        await File.WriteAllTextAsync(full, "base\n");
        await fixture.GitAsync("add", "--", path);
        var reader = new CliFileDiffReader(new());
        var unborn = Assert.Single((await fixture.ReadAsync()).Files);
        Assert.Contains((await reader.ReadAsync(fixture.Root, unborn, true, false, fixture.Token)).Lines, line => line.Text == "+base");
        await fixture.GitAsync("commit", "-m", "Initial fixture");
        await File.AppendAllTextAsync(full, "staged\n");
        await fixture.GitAsync("add", "--", path);
        await File.AppendAllTextAsync(full, "unstaged\n");
        // An unrelated huge diff must not be included in the selected request.
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "unrelated.txt"), new string('z', 6 * 1024 * 1024));
        var status = (await fixture.ReadAsync()).Files.Single(file => file.Path.Display == path);
        var index = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, ".git", "index"));
        var staged = await reader.ReadAsync(fixture.Root, status, true, false, fixture.Token);
        var unstaged = await reader.ReadAsync(fixture.Root, status, false, false, fixture.Token);
        Assert.Equal(DiffContentKind.Text, staged.Kind);
        Assert.Contains(staged.Lines, line => line.Text == "+staged");
        Assert.DoesNotContain(staged.Lines, line => line.Text.Contains("unstaged", StringComparison.Ordinal));
        Assert.Contains(unstaged.Lines, line => line.Text == "+unstaged");
        Assert.DoesNotContain(unstaged.Lines, line => line.Text == "+staged");
        Assert.All(staged.Metadata.Concat(unstaged.Metadata), file => Assert.Equal(path, file.Path.Display));
        Assert.Equal(index, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, ".git", "index")));
    }
    [Fact]
    public async Task WhenAFileIsRenamedAndThenDeleted_ShouldGiveTheRenameMetadataForBothPathsAndThePatchOfEachStep()
    {
        using var fixture = await GitFixture.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "old.txt"), "old content\n");
        await fixture.GitAsync("add", ".");
        await fixture.GitAsync("commit", "-m", "Initial fixture");
        await fixture.GitAsync("mv", "old.txt", "new č.txt");
        var reader = new CliFileDiffReader(new());
        var status = Assert.Single((await fixture.ReadAsync()).Files);
        var renamed = await reader.ReadAsync(fixture.Root, status, true, false, fixture.Token);
        Assert.Equal(2, renamed.Metadata.Count);
        Assert.Equal(FileDiffText.NoChanges, renamed.Message);
        Assert.DoesNotContain(renamed.Lines, line => line.IsChange);
        File.Delete(Path.Combine(fixture.Root, "new č.txt"));
        var deleted = await reader.ReadAsync(fixture.Root, Assert.Single((await fixture.ReadAsync()).Files), false, false, fixture.Token);
        Assert.Contains(deleted.Lines, line => line.Text == "-old content");
    }
    [Fact]
    public async Task WhenFilesAreBinaryHugeOrHaveLongOrManyRows_ShouldLimitTheirDiffsAndKeepEveryPreviewBoundedAlsoStagedAndDeleted()
    {
        using var fixture = await GitFixture.CreateAsync();
        var reader = new CliFileDiffReader(new());
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "binary"), [1, 0, 2, 3]);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "large"), string.Concat(Enumerable.Repeat("large text row\n", 400_000)));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "long"), new string('a', 9000));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "many"), string.Concat(Enumerable.Repeat("x\n", 50_001)));
        var snapshot = await fixture.ReadAsync();
        foreach (var file in snapshot.Files)
        {
            var result = await reader.ReadAsync(fixture.Root, file, false, false, fixture.Token);
            Assert.Empty(result.Lines);
            Assert.Equal(file.Path.Display == "binary" ? DiffContentKind.Binary : DiffContentKind.Limited, result.Kind);
            if (result.CanPreview)
            {
                var preview = await reader.ReadAsync(fixture.Root, file, false, true, fixture.Token);
                Assert.InRange(preview.Lines.Count, 1, 200);
            }
        }
        await fixture.GitAsync("add", "large");
        var large = (await fixture.ReadAsync()).Files.Single(file => file.Path.Display == "large");
        Assert.True((await reader.ReadAsync(fixture.Root, large, true, false, fixture.Token)).CanPreview);
        Assert.InRange((await reader.ReadAsync(fixture.Root, large, true, true, fixture.Token)).Lines.Count, 1, 200);
        File.Delete(Path.Combine(fixture.Root, "large"));
        var deleted = (await fixture.ReadAsync()).Files.Single(file => file.Path.Display == "large");
        var deletedPreview = await reader.ReadAsync(fixture.Root, deleted, false, true, fixture.Token);
        Assert.Equal(DiffContentKind.Limited, deletedPreview.Kind);
        Assert.InRange(deletedPreview.Lines.Count, 1, 200);
    }
    [Fact]
    public async Task WhenOnlySomeChangedPathsAreSignaled_ShouldReadTheStatusOfThosePathsOnly()
    {
        using var fixture = await GitFixture.CreateAsync();
        var reader = new CliRepositoryReader(new());
        var previous = await fixture.ReadAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "one.txt"), "one\n");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "two.txt"), "two\n");
        var incremental = await reader.ReadPathsAsync(previous, ["one.txt"], fixture.Token);
        Assert.Equal("one.txt", Assert.Single(incremental.Files).Path.Display);
        Assert.Equal(2, (await fixture.ReadAsync()).Files.Count);
    }

    // A signaled path that is listed already is read alone like any other: two.txt, changed back but not signaled,
    // keeps the row the previous read gave it, where a full read finds it clean.
    [Fact]
    public async Task WhenASignaledPathIsListedAlready_ShouldReadThatPathAloneAndKeepTheOtherRows()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("one.txt", "one\n");
        await fixture.WriteAsync("two.txt", "two\n");
        await fixture.CommitAsync("Both", "one.txt", "two.txt");
        await fixture.WriteAsync("one.txt", "one\nchanged\n");
        await fixture.WriteAsync("two.txt", "two\nchanged\n");
        var previous = await fixture.ReadAsync();
        File.Delete(Path.Combine(fixture.Root, "one.txt"));
        await fixture.WriteAsync("two.txt", "two\n");

        var incremental = await new CliRepositoryReader(new()).ReadPathsAsync(previous, ["one.txt"], fixture.Token);

        static IEnumerable<(string, char, char)> Rows(RepositorySnapshot snapshot) =>
            snapshot.Files.Select(file => (file.Path.Display, file.Index, file.Worktree)).Order();
        Assert.Equal([("one.txt", '.', 'M'), ("two.txt", '.', 'M')], Rows(previous));
        Assert.Equal([("one.txt", '.', 'D'), ("two.txt", '.', 'M')], Rows(incremental));
        Assert.Equal([("one.txt", '.', 'D')], Rows(await fixture.ReadAsync()));
    }
    // git rm --cached leaves the file on disk, and Git lists the index's deletion and the untracked file as two rows of
    // one path; a watcher update keeps both, as a full read does.
    [Fact]
    public async Task WhenAStagedDeletionAndAnUntrackedFileShareAPath_ShouldListBothAfterAWatcherUpdate()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "a\n");
        await fixture.CommitAsync("Add a", "a.txt");
        var previous = await fixture.ReadAsync();
        await fixture.GitAsync("rm", "--cached", "--quiet", "--", "a.txt");

        var incremental = await new CliRepositoryReader(new()).ReadPathsAsync(previous, ["a.txt"], fixture.Token);

        Assert.Equal([('?', '?'), ('D', '.')], incremental.Files.Select(file => (file.Index, file.Worktree)).Order());
        Assert.All(incremental.Files, file => Assert.Equal("a.txt", file.Path.Display));
    }

    // A name Git lists as untracked comes back by exactly its bytes from a full read and from a read of the signaled
    // name alone; x.txt, untracked beside it and not signaled, is what "[x].txt" would match as a glob.
    [Theory]
    [MemberData(nameof(HostileNameRows.Untracked), MemberType = typeof(HostileNameRows))]
    public async Task WhenAnUntrackedFileHasAHostileName_ShouldListItByItsExactBytesInAFullReadAndAWatcherRead(string label)
    {
        var name = TestNames.Find(label).Path;
        using var fixture = await GitFixture.CreateAsync();
        var previous = await fixture.ReadAsync();
        await fixture.WriteAsync(name, "hostile\n");
        await fixture.WriteAsync("x.txt", "decoy\n");

        var full = await fixture.ReadAsync();
        var watched = await new CliRepositoryReader(new()).ReadPathsAsync(previous, [name], fixture.Token);

        Assert.Equal(new[] { name, "x.txt" }.Order(StringComparer.Ordinal), full.Files.Select(file => file.Path.Display).Order(StringComparer.Ordinal));
        var row = Assert.Single(watched.Files);
        Assert.Equal(Encoding.UTF8.GetBytes(name), row.Path.Bytes);
        Assert.True(row.IsUntracked);
    }

    // A clone of a repository made where case tells names apart can hold two names that differ only in case; where the
    // file system ignores case they are one file, and an edit of it changes the status of both names. The watcher
    // signals the name on disk, and the row of the other name follows the edit as it does in a full read.
    [Fact]
    public async Task WhenTwoTrackedPathsDifferOnlyInCase_ShouldGiveTheSameRowsInAWatcherReadAsInAFullRead()
    {
        if (LocalPaths.Comparison == StringComparison.Ordinal) return;
        var (first, second) = TestNames.CaseTwins;
        using var fixture = await GitFixture.CreateAsync();
        // Under autocrlf Git lists a file rewritten with LF as changed although it diffs as unchanged.
        await fixture.GitAsync("config", "core.autocrlf", "false");
        foreach (var name in new[] { first, second })
        {
            var blob = await fixture.Runner.RunAsync(fixture.Root, ["hash-object", "-w", "--stdin"], fixture.Token, standardInput: Encoding.UTF8.GetBytes(name + "\n"));
            await fixture.GitAsync("update-index", "--add", "--cacheinfo", "100644," + Encoding.UTF8.GetString(blob.StandardOutput).Trim() + "," + name);
        }
        await fixture.GitAsync("commit", "-m", "Twins");
        await fixture.GitAsync("reset", "--hard");
        var onDisk = Path.GetFileName(Assert.Single(Directory.EnumerateFiles(fixture.Root)));
        var other = onDisk == first ? second : first;
        // The file holds the content of its own name, so only the other name is a change.
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, onDisk), onDisk + "\n");
        var previous = await fixture.ReadAsync();
        Assert.Equal(other, Assert.Single(previous.Files).Path.Display);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, onDisk), other + "\n");

        var incremental = await new CliRepositoryReader(new()).ReadPathsAsync(previous, [onDisk], fixture.Token);

        static IEnumerable<(string, char, char)> Rows(RepositorySnapshot snapshot) =>
            snapshot.Files.Select(file => (file.Path.Display, file.Index, file.Worktree)).Order();
        Assert.Equal(Rows(await fixture.ReadAsync()), Rows(incremental));
    }

    // The watcher signals a file inside a submodule as the submodule's own path; reading that path alone updates the
    // superproject's row for the submodule, with the changes it has of its own.
    [Fact]
    public async Task WhenAFileInsideASubmoduleChanges_ShouldUpdateTheSuperprojectRow()
    {
        using var fixture = await SubmoduleFixture.CreateAsync();
        var previous = await fixture.ReadAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "sub", "inside.txt"), "new\n");

        var incremental = await new CliRepositoryReader(new()).ReadPathsAsync(previous, ["sub"], fixture.Token);

        var row = Assert.Single(incremental.Files);
        Assert.Equal(("sub", true, true), (row.Path.Display, row.IsSubmodule, row.SubmoduleHasOwnChanges));
    }

    // A burst of signaled paths is one git call while the command line holds them; only names too long for one line
    // together split it.
    [Fact]
    public async Task WhenManyPathsAreSignaledAtOnce_ShouldReadThemInAsFewGitCallsAsTheCommandLineAllows()
    {
        using var fixture = await GitFixture.CreateAsync();
        var calls = 0;
        var reader = new CliRepositoryReader(new GitProcessRunner(exited: _ => Interlocked.Increment(ref calls)));
        var previous = await fixture.ReadAsync();
        var names = Enumerable.Range(0, 100).Select(i => "file-" + i + ".txt").ToArray();
        foreach (var name in names) await File.WriteAllTextAsync(Path.Combine(fixture.Root, name), name);
        Assert.Equal(100, (await reader.ReadPathsAsync(previous, names, fixture.Token)).Files.Count);
        Assert.Equal(1, Interlocked.Exchange(ref calls, 0));
        var gone = Enumerable.Range(0, 150).Select(i => i + new string('x', 200)).ToArray();
        Assert.Single((await reader.ReadPathsAsync(previous, [.. gone, names[0]], fixture.Token)).Files);
        Assert.Equal(2, calls);
    }
    // The folder of the open repository is deleted: the watch says its reads fail instead of ending or throwing, and a
    // refresh says the folder is no longer there.
    [Fact]
    public async Task WhenTheRepositoryFolderIsDeletedWhileOpen_ShouldReportFailedReadsAndRefreshShouldSayPathUnavailable()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "a\n");
        await fixture.CommitAsync("first", "a.txt");
        await using var workspace = new RepositoryWorkspace(new CliRepositoryReader(new()), new RepositoryWatcher(), new CliFileDiffReader(new()));
        await workspace.OpenAsync(fixture.Root, fixture.Token);
        using var watching = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        watching.CancelAfter(TimeSpan.FromSeconds(60));
        await using var changes = workspace.WatchAsync(watching.Token).GetAsyncEnumerator();

        foreach (var file in Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(fixture.Root, true);
        var failed = false;
        while (!failed && await changes.MoveNextAsync()) failed = changes.Current.Snapshot is null;
        var refresh = await Record.ExceptionAsync(() => workspace.RefreshAsync(fixture.Token));

        Assert.True(failed);
        Assert.Equal(RepositoryError.PathUnavailable, Assert.IsType<RepositoryException>(refresh).Error);
    }

    [Fact]
    public async Task WhenFilesAreEditedStagedCommittedRenamedAndMassIgnoredOutsideTrag_ShouldReconcileTheWatchedSnapshotWithGitUntilClosed()
    {
        using var fixture = await GitFixture.CreateAsync();
        await using var workspace = new RepositoryWorkspace(new CliRepositoryReader(new()), new RepositoryWatcher(), new CliFileDiffReader(new()));
        await workspace.OpenAsync(fixture.Root, fixture.Token);
        await using var changes = workspace.WatchAsync(fixture.Token).GetAsyncEnumerator();
        async Task<RepositorySnapshot> Until(Func<RepositorySnapshot, bool> predicate)
        {
            while (await changes.MoveNextAsync())
            {
                if (changes.Current.Snapshot is null) throw new InvalidOperationException("A watched read failed.");
                if (changes.Current.Snapshot is { } snapshot && predicate(snapshot)) return snapshot;
            }
            throw new InvalidOperationException("Watcher ended.");
        }
        await Until(_ => true);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "one.txt"), "one\n");
        await Until(s => s.Files.Any(file => file.Path.Display == "one.txt" && file.Worktree == '?'));
        await fixture.GitAsync("add", "one.txt");
        await Until(s => s.Files.Any(file => file.Path.Display == "one.txt" && file.IsStaged));
        await fixture.GitAsync("commit", "-m", "External fixture commit");
        await Until(s => s.Head is not null && s.Files.Count == 0);
        File.Move(Path.Combine(fixture.Root, "one.txt"), Path.Combine(fixture.Root, "renamed.txt"));
        await Until(s => s.Files.Any(file => file.Path.Display == "renamed.txt"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, ".gitignore"), "*.tmp\n");
        for (var i = 0; i < 600; i++) await File.WriteAllTextAsync(Path.Combine(fixture.Root, $"mass-{i}.tmp"), "ignored");
        await Until(s => s.Files.Any(file => file.Path.Display == ".gitignore") && s.Files.All(file => !file.Path.Display.EndsWith(".tmp", StringComparison.Ordinal)));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, ".gitignore"), "");
        var final = await Until(s => s.Files.Count == 603);
        Assert.Equal((await fixture.ReadAsync()).Files.Select(f => f.Path.Display).Order(), final.Files.Select(f => f.Path.Display).Order());
        await workspace.CloseAsync();
        Assert.False(workspace.IsCurrent(final));
    }
}
