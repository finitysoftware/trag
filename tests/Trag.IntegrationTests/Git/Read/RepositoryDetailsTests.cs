using System.Security.Cryptography;
using Trag.Domain.Details;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// What the repository's details read from Git: the current branch's history, the people who wrote it as .mailmap names
// them, and what the repository holds.
public sealed class RepositoryDetailsTests
{
    private static Task<RepositoryDetails> ReadAsync(GitFixture f, string? root = null, bool unborn = false, bool lfsFiles = false) =>
        new CliHistoryReader(f.Runner).ReadDetailsAsync(root ?? f.Root, unborn, lfsFiles, f.Token);

    // The commits of the current branch, those of every local and remote branch together, and when the branch's first
    // and latest commits were written.
    [Fact]
    public async Task WhenTheBranchHasCommits_ShouldCountThemAndEveryBranchsAndDateItsFirstAndLatest()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("commit", "--allow-empty", "-m", "first", "--date=2026-01-02T03:04:05+00:00");
        await f.GitAsync("commit", "--allow-empty", "-m", "second", "--date=2026-03-04T05:06:07+00:00");
        await f.GitAsync("switch", "-c", "topic");
        await f.GitAsync("commit", "--allow-empty", "-m", "topic only");
        await f.GitAsync("update-ref", "refs/remotes/origin/elsewhere", await f.GitAsync("commit-tree", "-m", "remote only", "HEAD^{tree}"));
        await f.GitAsync("switch", "main");

        var history = (await ReadAsync(f)).History;

        Assert.Equal(new BranchHistory(2, 4, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero)), history);
    }

    // A detached HEAD's own commits and a tag's commits no branch holds are in the history's all-branches view, so the
    // count of every commit holds them too and is never smaller than the count up to this commit; a file named HEAD is
    // not the ref.
    [Fact]
    public async Task WhenDetachedWithCommitsOfItsOwn_ShouldNotCountFewerOnAllBranches()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("commit", "--allow-empty", "-m", "on main");
        await f.GitAsync("switch", "--detach");
        await f.GitAsync("commit", "--allow-empty", "-m", "tagged");
        await f.GitAsync("tag", "kept");
        await f.GitAsync("commit", "--allow-empty", "-m", "detached only");
        await f.WriteAsync("HEAD", "a file, not the ref");

        var detached = (await ReadAsync(f)).History;
        await f.GitAsync("switch", "main");
        var onMain = (await ReadAsync(f)).History;

        Assert.Equal((3, 3), (detached.Commits, detached.AllCommits));
        Assert.Equal((1, 2), (onMain.Commits, onMain.AllCommits));
    }

    // With log.showSignature on, Git prints the verification of a signed commit before its fields; the dates are still read.
    [Fact]
    public async Task WhenLogShowSignatureIsOn_ShouldStillReadTheDates()
    {
        using var f = await GitFixture.CreateAsync("main");
        await SshSigning.UseAsync(args => f.GitAsync(args), f.Container, f.Token);
        await f.GitAsync("commit", "--allow-empty", "-m", "signed", "--date=2026-01-02T03:04:05+00:00");
        await f.GitAsync("config", "log.showSignature", "true");

        var history = (await ReadAsync(f)).History;

        var date = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        Assert.Equal((date, date), (history.First, history.Latest));
    }

    // A branch that joined an unrelated history has two first commits; its history begins with the older one.
    [Fact]
    public async Task WhenTheBranchHasTwoRoots_ShouldDateItsFirstCommitByTheOlder()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("commit", "--allow-empty", "-m", "newer root", "--date=2026-05-01T00:00:00+00:00");
        await f.GitAsync("switch", "--orphan", "older");
        await f.GitAsync("commit", "--allow-empty", "-m", "older root", "--date=2025-05-01T00:00:00+00:00");
        await f.GitAsync("switch", "main");
        await f.GitAsync("merge", "--no-ff", "--allow-unrelated-histories", "-m", "join", "older");

        Assert.Equal(new DateTimeOffset(2025, 5, 1, 0, 0, 0, TimeSpan.Zero), (await ReadAsync(f)).History.First);
    }

    // The one with most commits first, as .mailmap names them, read as UTF-8 whatever the repository's log output encoding.
    [Fact]
    public async Task WhenPeopleWroteTheBranch_ShouldListThemByCommitsAsMailmapNamesThem()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "i18n.logOutputEncoding", "ISO-8859-1");
        await f.WriteAsync(".mailmap", "Ana Anić <ana@example.invalid> <ana.old@example.invalid>\n");
        await f.CommitAsync("mailmap", ".mailmap");
        await f.GitAsync("commit", "--allow-empty", "-m", "old", "--author=Ana <ana.old@example.invalid>");
        await f.GitAsync("commit", "--allow-empty", "-m", "new", "--author=Ana Anić <ana@example.invalid>");
        await f.GitAsync("commit", "--allow-empty", "-m", "jose", "--author=José M. <jose@example.invalid>");

        var people = (await ReadAsync(f)).People;

        Assert.Equal([new Contributor(new("Ana Anić", "ana@example.invalid"), 2), new Contributor(new("José M.", "jose@example.invalid"), 1),
            new Contributor(new("Trag Fixture", "fixture@example.invalid"), 1)], people);
    }

    // Local branches, remote branches without the remote's HEAD, tags of both kinds, and stashes.
    [Fact]
    public async Task WhenTheRepositoryHasRefsAndStashes_ShouldCountEachKind()
    {
        using var f = await GitFixture.CreateAsync("main");
        var head = await f.CommitAsync("base");
        await f.GitAsync("branch", "topic");
        await f.GitAsync("update-ref", "refs/remotes/origin/main", head);
        await f.GitAsync("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main");
        await f.GitAsync("tag", "v1");
        await f.GitAsync("tag", "-a", "v2", "-m", "two");
        for (var i = 0; i < 2; i++)
        {
            await f.WriteAsync("a.txt", "change " + i + "\n");
            await f.GitAsync("stash", "push", "--include-untracked");
        }

        var contents = (await ReadAsync(f)).Contents;

        Assert.Equal((2, 1, 2, 2), (contents.LocalBranches, contents.RemoteBranches, contents.Tags, contents.Stashes));
    }

    // A submodule counts while the index holds it; .gitmodules can still name one that is gone.
    [Fact]
    public async Task WhenTheIndexHasASubmodule_ShouldCountOnlyThatOne()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        await f.GitAsync("config", "--file", ".gitmodules", "submodule.gone.path", "gone");

        Assert.Equal(1, (await ReadAsync(f)).Contents.Submodules);
    }

    // The current commit's Git LFS files, counted only where the snapshot found some.
    [Fact]
    public async Task WhenTheCommitHasGitLfsFiles_ShouldCountThem()
    {
        using var f = await GitFixture.CreateAsync("main");
        await LfsConfig.SetAsync(f, f.Root, installed: true);
        await f.WriteAsync(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        await f.WriteAsync("a.bin", "a\n");
        await f.WriteAsync("b.bin", "b\n");
        await f.WriteAsync("c.txt", "c\n");
        await f.CommitAsync("Assets");

        Assert.Equal(2, (await ReadAsync(f, lfsFiles: true)).Contents.LfsFiles);
        Assert.Equal(0, (await ReadAsync(f, lfsFiles: false)).Contents.LfsFiles);
    }

    // What the Git folder takes on disk, the objects among it.
    [Fact]
    public async Task WhenTheRepositoryHoldsALargeObject_ShouldCountItInTheGitFolderSize()
    {
        using var f = await GitFixture.CreateAsync("main");
        await File.WriteAllBytesAsync(Path.Combine(f.Root, "noise.bin"), RandomNumberGenerator.GetBytes(300_000), f.Token);
        await f.CommitAsync("Noise");

        Assert.InRange((await ReadAsync(f)).Contents.GitSize, 300_000, 10_000_000);
    }

    [Fact]
    public async Task WhenTheCloneIsShallow_ShouldSaySo()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("one");
        await f.CommitAsync("two");
        var shallow = Path.Combine(f.Container, "shallow");
        await f.GitAtAsync(f.Container, "clone", "--depth", "1", new Uri(f.Root).AbsoluteUri, shallow);

        Assert.False((await ReadAsync(f)).Contents.IsShallow);
        Assert.True((await ReadAsync(f, shallow)).Contents.IsShallow);
    }

    // The worktrees besides the main one.
    [Fact]
    public async Task WhenTheRepositoryHasAnotherWorktree_ShouldCountItBesidesTheMainOne()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("base");
        var alone = (await ReadAsync(f)).Contents.Worktrees;
        await f.GitAsync("worktree", "add", "-b", "second", Path.Combine(f.Container, "second"));

        Assert.Equal((0, 1), (alone, (await ReadAsync(f)).Contents.Worktrees));
    }

    // A commit from an import or another tool may carry an author date Git cannot read (a negative number of seconds,
    // nothing at all); Git prints %aI for it as it is, and the details are still read, without the date Git could not.
    [Theory]
    [InlineData("first")]
    [InlineData("latest")]
    public async Task WhenACommitHasAnAuthorDateGitCannotRead_ShouldReadTheDetailsWithoutThatDate(string unreadable)
    {
        using var f = await GitFixture.CreateAsync("main");
        var tree = await f.GitAsync("write-tree");
        var root = await CommitObjectAsync(f, tree, null, unreadable == "first" ? "-1 +0000" : "1767323045 +0000");
        await f.GitAsync("update-ref", "refs/heads/main", await CommitObjectAsync(f, tree, root, unreadable == "latest" ? "" : "1772600767 +0000"));

        var history = (await ReadAsync(f)).History;

        Assert.Equal(new BranchHistory(2, 2, unreadable == "first" ? null : new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            unreadable == "latest" ? null : new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero)), history);
    }

    // A commit written straight into the object database, which takes an author date Git itself would refuse to write.
    private static async Task<string> CommitObjectAsync(GitFixture f, string tree, string? parent, string date)
    {
        var text = "tree " + tree + "\n" + (parent is null ? "" : "parent " + parent + "\n") + "author Author <author@example.invalid> " + date + "\n"
            + "committer Author <author@example.invalid> 1772600767 +0000\n\nImported\n";
        var result = await f.Runner.RunAsync(f.Root, ["hash-object", "-t", "commit", "-w", "--literally", "--stdin"], f.Token, standardInput: System.Text.Encoding.UTF8.GetBytes(text));
        Assert.True(result.ExitCode == 0, System.Text.Encoding.UTF8.GetString(result.StandardError));
        return System.Text.Encoding.ASCII.GetString(result.StandardOutput).Trim();
    }

    // A branch without commits has no history and nobody behind it, and reading it is no failure.
    [Fact]
    public async Task WhenTheBranchHasNoCommits_ShouldReadNoHistoryAndNoPeople()
    {
        using var f = await GitFixture.CreateAsync("main");

        var details = await ReadAsync(f, unborn: true);

        Assert.Equal(BranchHistory.None, details.History);
        Assert.Empty(details.People);
    }
}
