using System.Text;
using Trag.Domain.History;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// One file's history as Trag reads it from real Git: only the commits that change the file, newest first, each with the
// path the file has in it, across a rename, a merge that brings the change in, pages and every branch.
public sealed class FileHistoryTests
{
    private static Task<HistoryPage> ReadAsync(GitFixture f, string head, string path, int page = 0, bool allBranches = false) =>
        new CliHistoryReader(new()).ReadPageAsync(f.Root, head, new("", page, allBranches) { Path = path }, f.Token);

    private static IEnumerable<(string Id, string Path, string? OriginalPath)> Steps(HistoryPage page) =>
        page.Commits.Select(commit => (commit.Id, commit.Path!.Display, commit.OriginalPath?.Display));

    // The older commits have the old name, and the rename says what the file was called; the name is taken literally.
    [Fact]
    public async Task WhenTheHistoryFollowsAFile_ShouldListOnlyItsCommitsWithItsPathInEachAcrossARename()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("old č [x].txt", "1\n"); var added = await f.CommitAsync("Add");
        await f.WriteAsync("other.txt", "o\n"); await f.CommitAsync("Other");
        await f.GitAsync("mv", "--", "old č [x].txt", "new č [x].txt"); var renamed = await f.CommitAsync("Rename");
        await f.WriteAsync("new č [x].txt", "2\n"); var changed = await f.CommitAsync("Change");

        var page = await ReadAsync(f, changed, "new č [x].txt");

        Assert.Equal([(changed, "new č [x].txt", null), (renamed, "new č [x].txt", "old č [x].txt"), (added, "old č [x].txt", null)], Steps(page));
        Assert.False(page.HasMore);
    }

    // A file renamed to the name: its history follows it back to its old name, and the commit that changed only x.txt,
    // what "[x].txt" would match as a glob, is not in it.
    [Theory]
    [MemberData(nameof(HostileNameRows.Tracked), MemberType = typeof(HostileNameRows))]
    public async Task WhenAFileHasAHostileName_ShouldListItsCommitsAcrossARenameToThatName(string label)
    {
        var name = TestNames.Find(label).Path;
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("plain.txt", "1\n"); var added = await f.CommitAsync("Add");
        await f.WriteAsync("x.txt", "o\n"); await f.CommitAsync("Decoy");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(f.Root, name))!);
        await f.GitAsync("mv", "--", "plain.txt", name); var renamed = await f.CommitAsync("Rename");
        await f.WriteAsync(name, "2\n"); var changed = await f.CommitAsync("Change");

        var page = await ReadAsync(f, changed, name);

        Assert.Equal([(changed, name, null), (renamed, name, "plain.txt"), (added, "plain.txt", null)], Steps(page));
    }

    // log.showRoot=false has Git's log give the first commit no changes; the commit that added the file there is still
    // in its history.
    [Fact]
    public async Task WhenTheConfigurationHidesTheFirstCommitsChanges_ShouldStillListTheCommitThatAddedTheFile()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "log.showRoot", "false");
        await f.WriteAsync("a.txt", "1\n"); var added = await f.CommitAsync("Add");
        await f.WriteAsync("a.txt", "2\n"); var changed = await f.CommitAsync("Change");

        var page = await ReadAsync(f, changed, "a.txt");

        Assert.Equal([changed, added], page.Commits.Select(commit => commit.Id));
    }

    // A file deleted at HEAD still has its history: the deletion first, then the commits that changed it.
    [Fact]
    public async Task WhenTheFileIsDeletedAtHead_ShouldStillListItsHistoryFromACommitThatHadIt()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("gone.txt", "1\n"); var added = await f.CommitAsync("Add");
        await f.WriteAsync("gone.txt", "2\n"); var changed = await f.CommitAsync("Change");
        await f.GitAsync("rm", "--quiet", "gone.txt"); var deleted = await f.CommitAsync("Delete");

        var page = await ReadAsync(f, deleted, "gone.txt");

        Assert.Equal([deleted, changed, added], page.Commits.Select(commit => commit.Id));
    }

    // A search within one file's history keeps both: the file's commits, and only those that match.
    [Fact]
    public async Task WhenHistoryIsSearchedWithAFilePath_ShouldApplyBothFilters()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "1\n"); var wanted = await f.CommitAsync("needle in a");
        await f.WriteAsync("b.txt", "1\n"); await f.CommitAsync("needle in b");
        await f.WriteAsync("a.txt", "2\n"); var head = await f.CommitAsync("hay in a");

        var page = await new CliHistoryReader(new()).ReadPageAsync(f.Root, head, new("needle") { Path = "a.txt" }, f.Token);

        Assert.Equal([wanted], page.Commits.Select(commit => commit.Id));
    }

    // Git takes a copy for where the file came from, and follows the original further back.
    [Fact]
    public async Task WhenTheFileStartedAsACopy_ShouldFollowTheOriginalBeforeIt()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "one\ntwo\nthree\nfour\n"); var added = await f.CommitAsync("Add");
        await f.WriteAsync("b.txt", "one\ntwo\nthree\nfour\n"); var copied = await f.CommitAsync("Copy");
        await f.WriteAsync("b.txt", "one\ntwo\nthree\nfive\n"); var changed = await f.CommitAsync("Change");

        Assert.Equal([(changed, "b.txt", null), (copied, "b.txt", "a.txt"), (added, "a.txt", null)], Steps(await ReadAsync(f, changed, "b.txt")));
    }

    [Fact]
    public async Task WhenAMergeBringsInTheFilesChange_ShouldListTheMergeAndTheChange()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "1\n"); var added = await f.CommitAsync("Add");
        await f.GitAsync("switch", "-c", "side");
        await f.WriteAsync("a.txt", "side\n"); var side = await f.CommitAsync("Side");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("other.txt", "o\n"); await f.CommitAsync("Other");
        await f.GitAsync("merge", "--no-ff", "-m", "Merge side", "side");
        var merge = await f.GitAsync("rev-parse", "HEAD");

        Assert.Equal([(merge, "a.txt", null), (side, "a.txt", null), (added, "a.txt", null)], Steps(await ReadAsync(f, merge, "a.txt")));
    }

    // A merge that kept the first parent's version changes nothing in the file against its first parent, which is how
    // the history compares a merge: it is left out, and the side's change is listed, as Git's log --follow lists them.
    [Fact]
    public async Task WhenAMergeHasAnEmptyFirstParentDiff_ShouldLeaveTheMergeOutAndListTheSidesChange()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "1\n"); var added = await f.CommitAsync("Add");
        await f.GitAsync("switch", "-c", "side");
        await f.WriteAsync("a.txt", "side\n"); var side = await f.CommitAsync("Side");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("other.txt", "o\n"); await f.CommitAsync("Other");
        await f.GitAsync("merge", "-s", "ours", "--no-edit", "side");
        var merge = await f.GitAsync("rev-parse", "HEAD");

        Assert.Equal([(side, "a.txt", null), (added, "a.txt", null)], Steps(await ReadAsync(f, merge, "a.txt")));
    }

    // 104 of 207 commits change the file: two pages, without gaps, in Git's own order.
    [Fact]
    public async Task WhenAFilesHistoryIsLongerThanAPage_ShouldPageItWithoutGaps()
    {
        using var f = await GitFixture.CreateAsync("main");
        var input = new StringBuilder();
        for (var i = 0; i < 207; i++)
        {
            var (path, content) = (i % 2 == 0 ? "f.txt" : "g.txt", "line " + i + "\n");
            input.Append($"commit refs/heads/main\ncommitter Fixture <fixture@example.invalid> {1700000000 + i} +0000\ndata 10\nCommit {i:D3}\n")
                .Append($"M 100644 inline {path}\ndata {Encoding.UTF8.GetByteCount(content)}\n{content}\n");
        }
        input.Append("done\n");
        Assert.Equal(0, (await new GitProcessRunner().RunAsync(f.Root, ["fast-import", "--quiet"], f.Token, standardInput: Encoding.UTF8.GetBytes(input.ToString()))).ExitCode);
        var head = await f.GitAsync("rev-parse", "main");

        var (first, second) = (await ReadAsync(f, head, "f.txt"), await ReadAsync(f, head, "f.txt", 1));

        Assert.Equal((100, true, 4, false), (first.Commits.Count, first.HasMore, second.Commits.Count, second.HasMore));
        Assert.Equal((await f.GitAsync("log", "--follow", "--root", "--format=%H", head, "--", "f.txt")).Split('\n'), first.Commits.Concat(second.Commits).Select(commit => commit.Id));
    }

    [Fact]
    public async Task WhenEveryBranchIsShown_ShouldListTheFilesCommitsOnAnotherBranchToo()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "1\n"); var added = await f.CommitAsync("Add");
        await f.GitAsync("switch", "-c", "side");
        await f.WriteAsync("a.txt", "side\n"); var side = await f.CommitAsync("Side");
        await f.WriteAsync("b.txt", "b\n"); await f.CommitAsync("Side other");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("c.txt", "c\n"); var head = await f.CommitAsync("Main other");

        Assert.Equal([added], (await ReadAsync(f, head, "a.txt")).Commits.Select(commit => commit.Id));
        Assert.Equal([side, added], (await ReadAsync(f, head, "a.txt", allBranches: true)).Commits.Select(commit => commit.Id));
    }

    // The status and a commit's files list a submodule whatever the configuration ignores, so its history lists the
    // commits that moved it too; Git's log would leave them all out.
    [Theory]
    [InlineData("diff.ignoreSubmodules")]
    [InlineData("submodule.sub.ignore")]
    public async Task WhenTheConfigurationIgnoresSubmodules_ShouldStillListTheCommitsThatMovedTheSubmodule(string key)
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var added = await f.GitAsync("rev-parse", "HEAD");
        await f.GitAtAsync(Path.Combine(f.Root, "sub"), "commit", "--allow-empty", "-m", "Sub two");
        var moved = await f.CommitAsync("Move sub", "sub");
        await f.GitAsync("config", key, "all");

        Assert.Equal([(moved, "sub", null), (added, "sub", null)], Steps(await ReadAsync(f, moved, "sub")));
    }
}
