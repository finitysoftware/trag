using Trag.Domain;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// A file of the working tree given back the content a commit left it with, by real Git: only the working tree changes,
// so what is staged stays staged, and a file that already has that content is left alone and said to be.
public sealed class RestoreFileTests
{
    private static async Task<(GitFixture Fixture, string First)> TwoVersionsAsync()
    {
        var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("a b.txt", "one\n");
        var first = await f.CommitAsync("one", "a b.txt");
        await f.WriteAsync("a b.txt", "two\n");
        await f.CommitAsync("two", "a b.txt");
        return (f, first);
    }

    private static async Task<GitWriteResult> RestoreAsync(GitFixture f, string commit, string path) =>
        await new GitWriteRouter(f.Runner).ExecuteAsync(new RestoreFileRequest(await f.ReadAsync(), commit, Snapshots.Path(path)), f.Token);

    [Fact]
    public async Task WhenAFileIsRestoredFromACommit_ShouldGiveTheWorkingTreeItsContentAndLeaveTheIndex()
    {
        var (f, first) = await TwoVersionsAsync();
        using var _ = f;
        await f.WriteAsync("a b.txt", "staged\n");
        await f.GitAsync("add", "a b.txt");

        var result = await RestoreAsync(f, first, "a b.txt");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(FileVersionText.Restored("a b.txt", GitObjectId.Short(first)), result.Message);
        Assert.Equal("one\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "a b.txt"), f.Token));
        Assert.Equal("staged", await f.GitAsync("show", ":a b.txt"));
    }

    // x.txt, changed in the same commits and what "[x].txt" would match as a glob, keeps its later content.
    [Theory]
    [MemberData(nameof(HostileNameRows.Tracked), MemberType = typeof(HostileNameRows))]
    public async Task WhenAFileHasAHostileName_ShouldRestoreItFromACommitAndLeaveTheIndex(string label)
    {
        var name = TestNames.Find(label).Path;
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync(name, "one\n"); await f.WriteAsync("x.txt", "decoy one\n");
        var first = await f.CommitAsync("one");
        await f.WriteAsync(name, "two\n"); await f.WriteAsync("x.txt", "decoy two\n");
        await f.CommitAsync("two");
        await f.WriteAsync(name, "staged\n");
        await f.GitAsync("add", "--", name);

        var result = await RestoreAsync(f, first, name);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("one\n", await File.ReadAllTextAsync(Path.Combine(f.Root, name), f.Token));
        Assert.Equal("staged", await f.GitAsync("show", ":" + name, "--"));
        Assert.Equal("decoy two\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "x.txt"), f.Token));
    }

    // The commit the file was last changed in, or the one shown at the top, gives the file what it already has; with
    // autocrlf, a working file with CRLF has the content its LF blob gives.
    [Fact]
    public async Task WhenTheFileAlreadyHasTheCommitsContent_ShouldSaySoAndNotClaimARestore()
    {
        var (f, _) = await TwoVersionsAsync();
        using var __ = f;
        await f.GitAsync("config", "core.autocrlf", "true");
        await f.WriteAsync("a b.txt", "two\r\n");
        var head = await f.GitAsync("rev-parse", "HEAD");

        var result = await RestoreAsync(f, head, "a b.txt");

        Assert.Equal((GitWriteOutcome.Succeeded, FileVersionText.AlreadyRestored("a b.txt", GitObjectId.Short(head))), (result.Outcome, result.Message));
        Assert.Equal("two\r\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "a b.txt"), f.Token));
    }

    // A commit gone from the repository since the history was shown, as after a rewrite.
    [Fact]
    public async Task WhenTheCommitIsGone_ShouldSayItWasNotRestoredAndLeaveTheFile()
    {
        var (f, _) = await TwoVersionsAsync();
        using var __ = f;

        var result = await RestoreAsync(f, new string('d', 40), "a b.txt");

        Assert.Equal((GitWriteOutcome.Rejected, FileVersionText.NotRestored), (result.Outcome, result.Message));
        Assert.Equal("two\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "a b.txt"), f.Token));
    }

    // Whether the file already has the commit's content is Git's own comparison; the diff program the user configured
    // is never run for it.
    [Fact]
    public async Task WhenDiffExternalIsConfigured_ShouldNotRunIt()
    {
        var (f, first) = await TwoVersionsAsync();
        using var __ = f;
        var marker = Path.Combine(f.Container, "external-ran");
        var script = Path.Combine(f.Container, "external.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\necho ran > '" + marker.Replace('\\', '/') + "'\n", f.Token);
        await f.GitAsync("config", "diff.external", script.Replace('\\', '/'));

        var result = await RestoreAsync(f, first, "a b.txt");

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.False(File.Exists(marker));
        Assert.Equal("one\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "a b.txt"), f.Token));
    }

    [Fact]
    public async Task WhenTheFilesFolderWasDeleted_ShouldRecreateItAndRestoreTheFile()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("deep/folder/a.txt", "one\n");
        var first = await f.CommitAsync("one", "deep/folder/a.txt");
        Directory.Delete(Path.Combine(f.Root, "deep"), true);

        var result = await RestoreAsync(f, first, "deep/folder/a.txt");

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("one\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "deep", "folder", "a.txt"), f.Token));
    }
}
