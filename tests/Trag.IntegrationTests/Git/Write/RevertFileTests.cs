using Trag.Domain;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// What one commit did to one file undone in the working tree, by real Git: the commit's diff of that file applied in
// reverse, so later changes to the file stay, the index is left alone, and a file changed since in the same lines is
// refused rather than merged. The repository judges whitespace strictly, and the first line ends in a space.
public sealed class RevertFileTests
{
    private const string Start = "one \r\ntwo\r\nthree\r\nfour\r\nfive\r\n";

    private static async Task<GitFixture> RepositoryAsync()
    {
        var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "true");
        await f.GitAsync("config", "apply.whitespace", "error");
        await f.WriteAsync("src/a.txt", Start);
        await f.CommitAsync("start", "src/a.txt");
        return f;
    }

    private static async Task<GitWriteResult> RevertAsync(GitFixture f, string commit, string path, string? original = null) =>
        await new GitWriteRouter(f.Runner).ExecuteAsync(
            new RevertFileRequest(await f.ReadAsync(), commit, Snapshots.Path(path), original is null ? null : Snapshots.Path(original)), f.Token);

    private static Task<string> TextAsync(GitFixture f, string path) => File.ReadAllTextAsync(Path.Combine(f.Root, path), f.Token);

    // x.txt, changed by the same commit and what "[x].txt" would match as a glob, keeps that change.
    [Theory]
    [MemberData(nameof(HostileNameRows.Tracked), MemberType = typeof(HostileNameRows))]
    public async Task WhenAFileHasAHostileName_ShouldUndoACommitsChangeToIt(string label)
    {
        var name = TestNames.Find(label).Path;
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync(name, "one\ntwo\nthree\nfour\nfive\n"); await f.WriteAsync("x.txt", "one\ntwo\nthree\nfour\nfive\n");
        await f.CommitAsync("start");
        await f.WriteAsync(name, "ONE\ntwo\nthree\nfour\nfive\n"); await f.WriteAsync("x.txt", "ONE\ntwo\nthree\nfour\nfive\n");
        var change = await f.CommitAsync("first line");
        await f.WriteAsync(name, "ONE\ntwo\nthree\nfour\nFIVE\n");
        await f.CommitAsync("last line", name);

        var result = await RevertAsync(f, change, name);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(("one\ntwo\nthree\nfour\nFIVE\n", "ONE\ntwo\nthree\nfour\nfive\n"), (await TextAsync(f, name), await TextAsync(f, "x.txt")));
        Assert.Equal("", await f.GitAsync("diff", "--cached", "--name-only"));
    }

    // The working file keeps its CRLF line endings, as autocrlf gives them.
    [Fact]
    public async Task WhenACommitsChangesToAFileAreUndone_ShouldKeepTheLaterChangesAndLeaveTheIndex()
    {
        using var f = await RepositoryAsync();
        await f.WriteAsync("src/a.txt", "ONE\r\ntwo\r\nthree\r\nfour\r\nfive\r\n");
        var change = await f.CommitAsync("first line", "src/a.txt");
        await f.WriteAsync("src/a.txt", "ONE\r\ntwo\r\nthree\r\nfour\r\nFIVE\r\n");
        await f.CommitAsync("last line", "src/a.txt");

        var result = await RevertAsync(f, change, "src/a.txt");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(FileVersionText.Reverted("src/a.txt", GitObjectId.Short(change)), result.Message);
        Assert.Equal("one \r\ntwo\r\nthree\r\nfour\r\nFIVE\r\n", await TextAsync(f, "src/a.txt"));
        Assert.Equal("", await f.GitAsync("diff", "--cached", "--name-only"));
    }

    [Fact]
    public async Task WhenTheSameLinesChangedSince_ShouldSaySoAndLeaveTheFile()
    {
        using var f = await RepositoryAsync();
        await f.WriteAsync("src/a.txt", "ONE\r\ntwo\r\nthree\r\nfour\r\nfive\r\n");
        var change = await f.CommitAsync("first line", "src/a.txt");
        await f.WriteAsync("src/a.txt", "Uno\r\ntwo\r\nthree\r\nfour\r\nfive\r\n");
        await f.CommitAsync("first line again", "src/a.txt");

        var result = await RevertAsync(f, change, "src/a.txt");

        Assert.Equal((GitWriteOutcome.Rejected, FileVersionText.NotReverted), (result.Outcome, result.Message));
        Assert.Equal("Uno\r\ntwo\r\nthree\r\nfour\r\nfive\r\n", await TextAsync(f, "src/a.txt"));
    }

    // A rename is undone under both names: the new one goes and the old one comes back with its content.
    [Fact]
    public async Task WhenTheCommitRenamedTheFile_ShouldBringTheOldNameBack()
    {
        using var f = await RepositoryAsync();
        await f.GitAsync("mv", "src/a.txt", "src/b.txt");
        var rename = await f.CommitAsync("rename");

        var result = await RevertAsync(f, rename, "src/b.txt", "src/a.txt");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.False(File.Exists(Path.Combine(f.Root, "src", "b.txt")));
        Assert.Equal(Start, await TextAsync(f, "src/a.txt"));
    }

    // A rename with a change in it, and a later change of the renamed file: the rename and its change are undone, the later
    // change stays under the old name.
    [Fact]
    public async Task WhenTheRenamedFileChangedLater_ShouldUndoTheRenameAndKeepTheLaterChange()
    {
        using var f = await RepositoryAsync();
        await f.GitAsync("mv", "src/a.txt", "src/b.txt");
        await f.WriteAsync("src/b.txt", "ONE\r\ntwo\r\nthree\r\nfour\r\nfive\r\n");
        var rename = await f.CommitAsync("rename and change", "src/b.txt");
        await f.WriteAsync("src/b.txt", "ONE\r\ntwo\r\nthree\r\nfour\r\nFIVE\r\n");
        await f.CommitAsync("later", "src/b.txt");

        var result = await RevertAsync(f, rename, "src/b.txt", "src/a.txt");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.False(File.Exists(Path.Combine(f.Root, "src", "b.txt")));
        Assert.Equal("one \r\ntwo\r\nthree\r\nfour\r\nFIVE\r\n", await TextAsync(f, "src/a.txt"));
    }

    // The file already has the lines the commit replaced: there is nothing to undo, and it says so.
    [Fact]
    public async Task WhenTheChangeIsAlreadyUndone_ShouldSaySoAndLeaveTheFile()
    {
        using var f = await RepositoryAsync();
        await f.WriteAsync("src/a.txt", "ONE\r\ntwo\r\nthree\r\nfour\r\nfive\r\n");
        var change = await f.CommitAsync("first line", "src/a.txt");
        await f.WriteAsync("src/a.txt", Start);
        await f.CommitAsync("first line back", "src/a.txt");

        var result = await RevertAsync(f, change, "src/a.txt");

        Assert.Equal((GitWriteOutcome.Rejected, FileVersionText.AlreadyReverted("src/a.txt", GitObjectId.Short(change))), (result.Outcome, result.Message));
        Assert.Equal(Start, await TextAsync(f, "src/a.txt"));
    }

    [Fact]
    public async Task WhenTheCommitIsGone_ShouldSaySo()
    {
        using var f = await RepositoryAsync();

        var result = await RevertAsync(f, new string('a', 40), "src/a.txt");

        Assert.Equal((GitWriteOutcome.Rejected, FileVersionText.RevertCommitGone), (result.Outcome, result.Message));
    }

    // A merge is undone against its first parent, as its file list compares it: what the merged branch brought goes.
    [Fact]
    public async Task WhenTheCommitIsAMerge_ShouldUndoWhatTheMergedBranchBrought()
    {
        using var f = await RepositoryAsync();
        await f.GitAsync("switch", "-c", "topic");
        await f.WriteAsync("src/a.txt", "one \r\ntwo\r\nthree\r\nfour\r\nFIVE\r\n");
        await f.CommitAsync("topic", "src/a.txt");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("src/a.txt", "ONE\r\ntwo\r\nthree\r\nfour\r\nfive\r\n");
        await f.CommitAsync("main", "src/a.txt");
        await f.GitAsync("merge", "--no-ff", "--no-edit", "topic");
        var merge = await f.GitAsync("rev-parse", "HEAD");

        var result = await RevertAsync(f, merge, "src/a.txt");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("ONE\r\ntwo\r\nthree\r\nfour\r\nfive\r\n", await TextAsync(f, "src/a.txt"));
    }

    // The first commit is undone against nothing: the file it added goes.
    [Fact]
    public async Task WhenTheCommitIsTheFirst_ShouldUndoTheFileItAdded()
    {
        using var f = await RepositoryAsync();
        var first = await f.GitAsync("rev-list", "--max-parents=0", "HEAD");

        var result = await RevertAsync(f, first, "src/a.txt");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.False(File.Exists(Path.Combine(f.Root, "src", "a.txt")));
    }

    [Fact]
    public async Task WhenTheFileIsBinary_ShouldGiveItBackItsBytes()
    {
        using var f = await RepositoryAsync();
        var logo = Path.Combine(f.Root, "src", "logo.bin");
        await File.WriteAllBytesAsync(logo, [0, 1, 2, 3], f.Token);
        await f.CommitAsync("logo", "src/logo.bin");
        await File.WriteAllBytesAsync(logo, [0, 9, 9, 3], f.Token);
        var change = await f.CommitAsync("new logo", "src/logo.bin");

        var result = await RevertAsync(f, change, "src/logo.bin");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal([0, 1, 2, 3], await File.ReadAllBytesAsync(logo, f.Token));
    }

    // Without core.fileMode the working tree keeps no executable bit, so a commit that only set it has nothing there to
    // undo: it says so rather than claiming the change undone, and the index keeps the mode.
    [Fact]
    public async Task WhenTheCommitOnlySetTheExecutableBitAndTheWorkingTreeKeepsNone_ShouldSaySoAndChangeNothing()
    {
        using var f = await RepositoryAsync();
        await f.GitAsync("config", "core.fileMode", "false");
        await f.GitAsync("update-index", "--chmod=+x", "src/a.txt");
        var change = await f.CommitAsync("executable");

        var result = await RevertAsync(f, change, "src/a.txt");

        Assert.Equal((GitWriteOutcome.Rejected, FileVersionText.ExecutableBitNotKept("src/a.txt", GitObjectId.Short(change))), (result.Outcome, result.Message));
        Assert.Equal(Start, await TextAsync(f, "src/a.txt"));
        Assert.StartsWith("100755", await f.GitAsync("ls-files", "--stage", "src/a.txt"));
    }

    // With core.fileMode the executable bit is Git's to handle, so a commit that only set it is not refused but handed to Git.
    [Fact]
    public async Task WhenTheCommitOnlySetTheExecutableBitAndTheWorkingTreeKeepsIt_ShouldUndoIt()
    {
        using var f = await RepositoryAsync();
        await f.GitAsync("config", "core.fileMode", "false");
        await f.GitAsync("update-index", "--chmod=+x", "src/a.txt");
        var change = await f.CommitAsync("executable");
        await f.GitAsync("config", "core.fileMode", "true");

        var result = await RevertAsync(f, change, "src/a.txt");

        Assert.Equal((GitWriteOutcome.Succeeded, FileVersionText.Reverted("src/a.txt", GitObjectId.Short(change))), (result.Outcome, result.Message));
    }

    // The bit set alongside a changed line: the line is undone, whatever the working tree keeps of the bit.
    [Fact]
    public async Task WhenTheCommitSetTheExecutableBitAndChangedALine_ShouldUndoTheLine()
    {
        using var f = await RepositoryAsync();
        await f.GitAsync("config", "core.fileMode", "false");
        await f.WriteAsync("src/a.txt", "ONE\r\ntwo\r\nthree\r\nfour\r\nfive\r\n");
        await f.GitAsync("add", "src/a.txt");
        await f.GitAsync("update-index", "--chmod=+x", "src/a.txt");
        var change = await f.CommitAsync("executable and first line");

        var result = await RevertAsync(f, change, "src/a.txt");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(Start, await TextAsync(f, "src/a.txt"));
    }

    // An empty file's patch has as few lines as one that only sets the bit, and undoing its addition takes it away.
    [Fact]
    public async Task WhenTheCommitAddedAnEmptyFile_ShouldTakeItAway()
    {
        using var f = await RepositoryAsync();
        await f.GitAsync("config", "core.fileMode", "false");
        await f.WriteAsync("src/empty.txt", "");
        var change = await f.CommitAsync("empty", "src/empty.txt");

        var result = await RevertAsync(f, change, "src/empty.txt");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.False(File.Exists(Path.Combine(f.Root, "src", "empty.txt")));
    }

    // Git refuses to write the file over a folder; the reason given is the folder, and what is in it stays.
    [Fact]
    public async Task WhenAFolderNowStandsWhereTheFileGoes_ShouldSaySoAndLeaveIt()
    {
        using var f = await RepositoryAsync();
        await f.WriteAsync("src/a.txt", "ONE\r\ntwo\r\nthree\r\nfour\r\nfive\r\n");
        var change = await f.CommitAsync("first line", "src/a.txt");
        File.Delete(Path.Combine(f.Root, "src", "a.txt"));
        await f.WriteAsync("src/a.txt/inner.txt", "inner");

        var result = await RevertAsync(f, change, "src/a.txt");

        Assert.Equal((GitWriteOutcome.Rejected, FileVersionText.FolderInTheWay("src/a.txt")), (result.Outcome, result.Message));
        Assert.Equal("inner", await TextAsync(f, "src/a.txt/inner.txt"));
    }

    // Undoing a rename, Git removes the new name before it fails to write the old one over a folder, and the new name's
    // uncommitted change would go with it.
    [Fact]
    public async Task WhenAFolderNowStandsAtTheOldNameOfARenamedFile_ShouldSaySoAndKeepTheRenamedFile()
    {
        using var f = await RepositoryAsync();
        await f.GitAsync("mv", "src/a.txt", "src/b.txt");
        var rename = await f.CommitAsync("rename");
        await f.WriteAsync("src/b.txt", "one \r\ntwo\r\nthree\r\nfour\r\nFIVE\r\n");
        await f.WriteAsync("src/a.txt/inner.txt", "inner");

        var result = await RevertAsync(f, rename, "src/b.txt", "src/a.txt");

        Assert.Equal((GitWriteOutcome.Rejected, FileVersionText.FolderInTheWay("src/a.txt")), (result.Outcome, result.Message));
        Assert.Equal("one \r\ntwo\r\nthree\r\nfour\r\nFIVE\r\n", await TextAsync(f, "src/b.txt"));
    }

    // Undoing a rename over a folder at the new name, Git cannot remove the folder, writes nothing back and still exits 0.
    [Fact]
    public async Task WhenAFolderNowStandsAtTheNewNameOfARenamedFile_ShouldSaySoAndLeaveIt()
    {
        using var f = await RepositoryAsync();
        await f.GitAsync("mv", "src/a.txt", "src/b.txt");
        var rename = await f.CommitAsync("rename");
        File.Delete(Path.Combine(f.Root, "src", "b.txt"));
        await f.WriteAsync("src/b.txt/inner.txt", "inner");

        var result = await RevertAsync(f, rename, "src/b.txt", "src/a.txt");

        Assert.Equal((GitWriteOutcome.Rejected, FileVersionText.FolderInTheWay("src/b.txt")), (result.Outcome, result.Message));
        Assert.Equal("inner", await TextAsync(f, "src/b.txt/inner.txt"));
    }

    // The revert is applied to the working file alone: a change staged meanwhile stays in the index as it was.
    [Fact]
    public async Task WhenTheIndexAlsoHoldsChanges_ShouldRevertOnlyTheWorkingFile()
    {
        using var f = await RepositoryAsync();
        await f.WriteAsync("src/a.txt", "ONE\r\ntwo\r\nthree\r\nfour\r\nfive\r\n");
        var change = await f.CommitAsync("first line", "src/a.txt");
        await f.WriteAsync("src/a.txt", "ONE\r\ntwo\r\nthree\r\nfour\r\nFIVE\r\n");
        await f.GitAsync("add", "src/a.txt");

        var result = await RevertAsync(f, change, "src/a.txt");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("one \r\ntwo\r\nthree\r\nfour\r\nFIVE\r\n", await TextAsync(f, "src/a.txt"));
        Assert.Equal("ONE\ntwo\nthree\nfour\nFIVE", await f.GitAsync("show", ":src/a.txt"));
    }
}
