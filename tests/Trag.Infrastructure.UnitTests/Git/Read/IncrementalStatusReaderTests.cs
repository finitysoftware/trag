using System.Text;
using Trag.Domain.Paths;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Read;

// The read of the paths the watcher signaled gives the rows a full read gives.
public sealed class IncrementalStatusReaderTests
{
    // Where the file system ignores case, two names the index holds that differ only in case are one file on disk, and
    // the watcher signals the name on disk. The other name, clean before, is changed by the edit, as a full read shows.
    [Fact]
    public async Task WhenAnEditChangesTheOtherNameOfTheSameFile_ShouldListItAsAFullReadDoes()
    {
        if (LocalPaths.Comparison == StringComparison.Ordinal) return;
        using var fixture = await GitFixture.CreateAsync();
        var (onDisk, other) = await CaseTwinsAsync(fixture);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, onDisk), other + "\n");
        var previous = await fixture.ReadAsync();
        Assert.Equal(onDisk, Assert.Single(previous.Files).Path.Display);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, onDisk), onDisk + "\n");

        var incremental = await new CliRepositoryReader(fixture.Runner).ReadPathsAsync(previous, [onDisk], fixture.Token);

        Assert.Equal(Rows(await fixture.ReadAsync()), Rows(incremental));
    }

    // Where the file system tells case apart, the two names are two files: a change to the one not signaled is not read
    // with the signaled one, and its row stays as it was until its own signal.
    [Fact]
    public async Task WhenTheFileSystemTellsCaseApart_ShouldReadOnlyTheSignaledNameOfTwoThatDifferInCase()
    {
        if (LocalPaths.Comparison != StringComparison.Ordinal) return;
        using var fixture = await GitFixture.CreateAsync();
        var (first, second) = TestNames.CaseTwins;
        await fixture.WriteAsync(first, "first\n");
        await fixture.WriteAsync(second, "second\n");
        await fixture.CommitAsync("Twins", first, second);
        var previous = await fixture.ReadAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, first), "changed\n");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, second), "changed\n");

        var incremental = await new CliRepositoryReader(fixture.Runner).ReadPathsAsync(previous, [first], fixture.Token);

        Assert.Equal([(first, null, '.', 'M')], Rows(incremental));
    }

    // A rename is one file with two names: a signal for either name reads both again, so Git pairs them as before.
    [Fact]
    public async Task WhenOneNameOfARenameIsSignaled_ShouldReadTheRenameWithBothNames()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("old.txt", "same content line\n");
        await fixture.CommitAsync("Old", "old.txt");
        await fixture.GitAsync("mv", "old.txt", "new.txt");
        var previous = await fixture.ReadAsync();
        await fixture.WriteAsync("old.txt", "again\n");

        var incremental = await new CliRepositoryReader(fixture.Runner).ReadPathsAsync(previous, ["old.txt"], fixture.Token);

        Assert.Equal([("new.txt", "old.txt", 'R', '.')], Rows(previous));
        Assert.Equal([("new.txt", "old.txt", 'R', '.'), ("old.txt", null, '?', '?')], Rows(incremental));
        Assert.Equal(Rows(await fixture.ReadAsync()), Rows(incremental));
    }

    // A name that is not UTF-8 cannot be handed to Git as text, so a rename with such a name is read with the whole status.
    [Fact]
    public async Task WhenARenamesOtherNameIsNotUtf8_ShouldReadTheWholeStatus()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("old.txt", "same content line\n");
        await fixture.CommitAsync("Old", "old.txt");
        var blob = await fixture.GitAsync("rev-parse", "HEAD:old.txt");
        await fixture.GitAsync("rm", "--cached", "--quiet", "--", "old.txt");
        await fixture.Runner.RunAsync(fixture.Root, ["update-index", "--index-info"], fixture.Token,
            standardInput: [.. Encoding.ASCII.GetBytes("100644 " + blob + " 0\t"), 0xFF, .. ".txt\n"u8]);
        var previous = await fixture.ReadAsync();
        Assert.Contains(previous.Files, file => !file.Path.IsDisplayable && file.OriginalPath?.Display == "old.txt");

        var incremental = await new CliRepositoryReader(fixture.Runner).ReadPathsAsync(previous, ["old.txt"], fixture.Token);

        Assert.Equal(Rows(await fixture.ReadAsync()), Rows(incremental));
    }

    // Many signaled paths split the read into several calls, and Git answers each call for both names of one file: each
    // row is listed once.
    [Fact]
    public async Task WhenBothNamesOfOneFileAreReadInSeparateCalls_ShouldListEachRowOnce()
    {
        if (LocalPaths.Comparison == StringComparison.Ordinal) return;
        using var fixture = await GitFixture.CreateAsync();
        var (onDisk, other) = await CaseTwinsAsync(fixture);
        var previous = await fixture.ReadAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, onDisk), "neither\n");
        var gone = Enumerable.Range(0, 120).Select(i => i + new string('x', 200));

        var incremental = await new CliRepositoryReader(fixture.Runner).ReadPathsAsync(previous, [onDisk, .. gone, other], fixture.Token);

        Assert.Equal(2, incremental.Files.Count);
        Assert.Equal(Rows(await fixture.ReadAsync()), Rows(incremental));
    }

    // A merge begun in a terminal stops on a conflict: a signaled file is read with the whole status, which names the merge.
    [Fact]
    public async Task WhenAMergeStoppedOnAConflictSinceThePreviousRead_ShouldReadTheWholeStatusWithTheMerge()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "base\n");
        await fixture.CommitAsync("Base", "a.txt");
        await fixture.GitAsync("switch", "--quiet", "-c", "other");
        await fixture.WriteAsync("a.txt", "theirs\n");
        await fixture.CommitAsync("Theirs", "a.txt");
        await fixture.GitAsync("switch", "--quiet", "-");
        await fixture.WriteAsync("a.txt", "ours\n");
        await fixture.CommitAsync("Ours", "a.txt");
        var previous = await fixture.ReadAsync();
        Assert.Equal(1, (await fixture.RunAsync("merge", "other")).ExitCode);

        var incremental = await new CliRepositoryReader(fixture.Runner).ReadPathsAsync(previous, ["a.txt"], fixture.Token);

        Assert.Equal(RepositoryOperation.Merge, incremental.Operation);
        Assert.Equal(Rows(await fixture.ReadAsync()), Rows(incremental));
    }

    // Where the file system ignores case, a new file named like a staged rename's old name in other case is that old name
    // to Git's case-blind pathspec, which lists the old name's deletion apart from the rename unless both names are read.
    [Fact]
    public async Task WhenASignaledNameDiffersOnlyInCaseFromARenamesOldName_ShouldListWhatAFullReadLists()
    {
        if (LocalPaths.Comparison == StringComparison.Ordinal) return;
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("A.TXT", "same content line\n");
        await fixture.CommitAsync("Old", "A.TXT");
        await fixture.GitAsync("mv", "A.TXT", "moved.txt");
        var previous = await fixture.ReadAsync();
        await fixture.WriteAsync("A.txt", "new\n");

        var incremental = await new CliRepositoryReader(fixture.Runner).ReadPathsAsync(previous, ["A.txt"], fixture.Token);

        Assert.Equal([("moved.txt", "A.TXT", 'R', '.')], Rows(previous));
        Assert.Equal(Rows(await fixture.ReadAsync()), Rows(incremental));
    }

    // The watcher names a removed folder by its own name; the rows listed under it, an edited tracked file and an untracked
    // one, are read with the whole status.
    [Fact]
    public async Task WhenASignaledNameWasAFolderWithListedRows_ShouldReadTheWholeStatus()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("dir/tracked.txt", "base\n");
        await fixture.CommitAsync("Tracked", "dir/tracked.txt");
        await fixture.WriteAsync("dir/tracked.txt", "edited\n");
        await fixture.WriteAsync("dir/new.txt", "new\n");
        var previous = await fixture.ReadAsync();
        Directory.Delete(Path.Combine(fixture.Root, "dir"), true);

        var incremental = await new CliRepositoryReader(fixture.Runner).ReadPathsAsync(previous, ["dir"], fixture.Token);

        Assert.Equal([("dir/tracked.txt", null, '.', 'D')], Rows(incremental));
    }

    // A file edited inside a repository nested in the working tree is signaled as the nested repository's folder, which
    // Git lists as one row ending in a slash; that row is the folder's own and is read alone.
    [Fact]
    public async Task WhenAFileInsideAnUntrackedNestedRepositoryChanges_ShouldReadItsRowAlone()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("init", "--quiet", "nested");
        await fixture.WriteAsync("nested/a.txt", "a\n");
        var previous = await fixture.ReadAsync();
        Assert.Equal([("nested/", null, '?', '?')], Rows(previous));
        await fixture.WriteAsync("nested/a.txt", "edited\n");
        var processes = 0;

        var incremental = await new CliRepositoryReader(new GitProcessRunner(exited: _ => Interlocked.Increment(ref processes)))
            .ReadPathsAsync(previous, ["nested"], fixture.Token);

        Assert.Equal(1, processes);
        Assert.Equal(Rows(await fixture.ReadAsync()), Rows(incremental));
    }

    // A nested repository's folder moved out of the working tree or sent to the Recycle Bin is signaled by its own name,
    // without the slash that ends the folder's row.
    [Fact]
    public async Task WhenAnUntrackedNestedRepositorysFolderIsRemoved_ShouldDropItsRow()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("init", "--quiet", "nested");
        await fixture.WriteAsync("nested/a.txt", "a\n");
        await fixture.WriteAsync("keep.txt", "keep\n");
        var previous = await fixture.ReadAsync();
        Directory.Delete(Path.Combine(fixture.Root, "nested"), true);

        var incremental = await new CliRepositoryReader(fixture.Runner).ReadPathsAsync(previous, ["nested"], fixture.Token);

        Assert.Equal([("keep.txt", null, '?', '?')], Rows(incremental));
    }

    // A nested repository's folder renamed in the working tree is signaled by both of its names, neither with the slash
    // that ends the folder's row.
    [Fact]
    public async Task WhenAnUntrackedNestedRepositorysFolderIsRenamed_ShouldListItUnderItsNewNameOnly()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("init", "--quiet", "nested");
        await fixture.WriteAsync("nested/a.txt", "a\n");
        var previous = await fixture.ReadAsync();
        Directory.Move(Path.Combine(fixture.Root, "nested"), Path.Combine(fixture.Root, "nested2"));

        var incremental = await new CliRepositoryReader(fixture.Runner).ReadPathsAsync(previous, ["nested", "nested2"], fixture.Token);

        Assert.Equal([("nested2/", null, '?', '?')], Rows(incremental));
    }

    // Two names that differ only in case, each committed with its own name as its content; one file holds them on disk.
    private static async Task<(string OnDisk, string Other)> CaseTwinsAsync(GitFixture fixture)
    {
        var (first, second) = TestNames.CaseTwins;
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
        return (onDisk, onDisk == first ? second : first);
    }

    private static IEnumerable<(string, string?, char, char)> Rows(RepositorySnapshot snapshot) =>
        snapshot.Files.Select(file => (file.Path.Display, file.OriginalPath?.Display, file.Index, file.Worktree)).Order();
}
