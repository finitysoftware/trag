using System.Text;
using Trag.TestSupport;
using Trag.Domain.Diff;
using Trag.Domain.Errors;
using Trag.Domain.History;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;

namespace Trag.IntegrationTests.Git.Read;

public sealed class HistoryTests
{
    // A commit with no message at all is listed with an empty subject, beside the others.
    [Fact]
    public async Task WhenACommitHasAnEmptyMessage_ShouldListItWithAnEmptySubject()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("commit", "--allow-empty", "-m", "first");
        await f.GitAsync("commit", "--allow-empty", "--allow-empty-message", "-m", "");
        var head = await f.GitAsync("rev-parse", "HEAD");

        var page = await new CliHistoryReader(new()).ReadPageAsync(f.Root, head, new(), f.Token);

        Assert.Equal(["", "first"], page.Commits.Select(commit => commit.Subject));
    }

    // A message written in another encoding without the header that would say so is shown as Git reads it, which takes
    // bytes that are not UTF-8 for Latin-1, and the page still reads.
    [Fact]
    public async Task WhenACommitMessageIsNotUtf8WithoutAnEncodingHeader_ShouldShowItAsGitReadsItAndStillPage()
    {
        using var f = await GitFixture.CreateAsync();
        var message = Path.Combine(f.Container, "message.txt");
        await File.WriteAllBytesAsync(message, [0x43, 0x61, 0x66, 0xE9, 0x0A]);
        await f.GitAsync("commit", "--allow-empty", "--cleanup=verbatim", "-F", message);
        var head = await f.GitAsync("rev-parse", "HEAD");

        var page = await new CliHistoryReader(new()).ReadPageAsync(f.Root, head, new(), f.Token);

        Assert.Equal("Café", Assert.Single(page.Commits).Subject);
    }

    // A history with two roots, joined by a merge of unrelated histories, and an octopus merge after it: the octopus
    // lists its three parents in the order Git keeps them, and both roots are on the page.
    [Fact]
    public async Task WhenTheRepositoryHasTwoRootsAndAnOctopusMerge_ShouldListAllParentsInOrder()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("main.txt", "main\n"); await f.CommitAsync("Main root", "main.txt");
        await f.GitAsync("switch", "--orphan", "other");
        await f.WriteAsync("other.txt", "other\n"); await f.CommitAsync("Other root", "other.txt");
        await f.GitAsync("switch", "main");
        await f.GitAsync("merge", "--no-ff", "--allow-unrelated-histories", "--no-edit", "other");
        foreach (var name in new[] { "b", "c" })
        {
            await f.GitAsync("switch", "-c", name, "main");
            await f.WriteAsync(name + ".txt", name + "\n"); await f.CommitAsync(name, name + ".txt");
        }
        await f.GitAsync("switch", "main");
        await f.WriteAsync("d.txt", "d\n"); var main = await f.CommitAsync("d", "d.txt");
        var (b, c) = (await f.GitAsync("rev-parse", "b"), await f.GitAsync("rev-parse", "c"));
        await f.GitAsync("merge", "--no-ff", "--no-edit", "b", "c");
        var head = await f.GitAsync("rev-parse", "HEAD");

        var page = await new CliHistoryReader(new()).ReadPageAsync(f.Root, head, new(), f.Token);

        Assert.Equal([main, b, c], page.Commits.Single(commit => commit.Id == head).Parents);
        Assert.Equal(2, page.Commits.Count(commit => commit.Parents.Count == 0));
    }

    [Fact]
    public async Task WhenTheHistoryIsLongerThanTwoPages_ShouldPageItWithoutGapsInGitsTopoOrder()
    {
        using var f = await GitFixture.CreateAsync();
        var (_, _, first, second, third) = await ThreePagesAsync(f);
        var all = first.Commits.Concat(second.Commits).Concat(third.Commits).ToArray();
        Assert.Equal(207, all.Select(commit => commit.Id).Distinct().Count());
        Assert.Equal([100, 100, 7], new[] { first.Commits.Count, second.Commits.Count, third.Commits.Count });
        Assert.True(first.HasMore); Assert.True(second.HasMore); Assert.False(third.HasMore);
        Assert.Equal((await Text(f, "log", "--topo-order", "--format=%H")).Split('\n', StringSplitOptions.RemoveEmptyEntries), all.Select(commit => commit.Id));
    }

    // The history shown read again in place reads all its pages at once: their commits in one read, numbered by the last.
    [Fact]
    public async Task WhenSeveralPagesAreReadAtOnce_ShouldGiveTheCommitsOfThosePagesAsPagedOneByOne()
    {
        using var f = await GitFixture.CreateAsync();
        var (head, reader, first, second, third) = await ThreePagesAsync(f);

        var opening = await reader.ReadPageAsync(f.Root, head, new("", 0) { Pages = 2 }, f.Token);
        var closing = await reader.ReadPageAsync(f.Root, head, new("", 1) { Pages = 2 }, f.Token);

        Assert.Equal(first.Commits.Concat(second.Commits).Select(commit => commit.Id), opening.Commits.Select(commit => commit.Id));
        Assert.Equal((true, 1), (opening.HasMore, opening.Number));
        Assert.Equal(second.Commits.Concat(third.Commits).Select(commit => commit.Id), closing.Commits.Select(commit => commit.Id));
        Assert.Equal((false, 2), (closing.HasMore, closing.Number));
    }

    [Fact]
    public async Task WhenTheHistoryIsSearched_ShouldFindCommitsByShaBodyAuthorOrEmailAndPageThemLikeThePlainHistory()
    {
        using var f = await GitFixture.CreateAsync();
        var (head, reader, first, second, third) = await ThreePagesAsync(f);
        var all = first.Commits.Concat(second.Commits).Concat(third.Commits).ToArray();
        var old = all[^1];
        foreach (var query in new[] { old.Id[..12], "distant body needle", "SPECIAL AUTHOR Č", "special@example.invalid" })
            Assert.Equal(old.Id, Assert.Single((await reader.ReadPageAsync(f.Root, head, new(query, 0), f.Token)).Commits).Id);
        var searched = await reader.ReadPageAsync(f.Root, head, new("Commit", 1), f.Token);
        Assert.Equal(second.Commits.Select(commit => commit.Id), searched.Commits.Select(commit => commit.Id));
        Assert.Empty((await reader.ReadPageAsync(f.Root, head, new("no-such-message", 0), f.Token)).Commits);
    }

    // A person is found under every address .mailmap gives them, and shown as it names them: a search for the name or
    // address it names finds the commits written under an older one too, and one for the older address still finds those.
    [Theory]
    [InlineData("ana@example.invalid", "new address", "old address")]
    [InlineData("Ana Anić", "new address", "old address")]
    [InlineData("ana.old@example.invalid", "old address")]
    public async Task WhenTheQueryIsWhoMailmapNames_ShouldFindTheirCommitsUnderEveryAddressAndShowThemAsItNamesThem(string query, params string[] subjects)
    {
        using var f = await MailmappedAsync();
        var head = await f.GitAsync("rev-parse", "HEAD");

        var found = (await new CliHistoryReader(f.Runner).ReadPageAsync(f.Root, head, new(query, 0), f.Token)).Commits;

        Assert.Equal(subjects.Select(subject => (subject, "Ana Anić", "ana@example.invalid")), found.Select(commit => (commit.Subject, commit.Author, commit.Email)));
    }

    // A commit's details name its author as .mailmap does, as the history shows them.
    [Fact]
    public async Task WhenACommitWrittenUnderAnOlderAddressIsRead_ShouldNameItsAuthorAsMailmapNamesThem()
    {
        using var f = await MailmappedAsync();

        var details = await new CliHistoryReader(f.Runner).ReadCommitAsync(f.Root, await f.GitAsync("rev-parse", "HEAD~1"), f.Token);

        Assert.Equal(("old address", "Ana Anić", "ana@example.invalid"), (details.Commit.Subject, details.Commit.Author, details.Commit.Email));
    }

    // Ana wrote under an older address first, then under the one .mailmap names her by.
    private static async Task<GitFixture> MailmappedAsync()
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.WriteAsync(".mailmap", "Ana Anić <ana@example.invalid> <ana.old@example.invalid>\n");
            await f.CommitAsync("mailmap", ".mailmap");
            await f.GitAsync("commit", "--allow-empty", "-m", "old address", "--author=Ana <ana.old@example.invalid>");
            await f.GitAsync("commit", "--allow-empty", "-m", "new address", "--author=Ana Anić <ana@example.invalid>");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    [Theory]
    [InlineData("--all")]
    [InlineData("HEAD~200")]
    [InlineData("$(touch unexpected) ; [a-z].* \"quote\"")]
    public async Task WhenTheQueryLooksLikeAnOptionARevisionOrAShellCommand_ShouldMatchItLiterallyAndOnlyInTheActiveHistory(string query)
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("commit", "--allow-empty", "-m", "Initial");
        await f.GitAsync("branch", "other");
        await f.GitAsync("commit", "--allow-empty", "-m", query);
        var head = (await Text(f, "rev-parse", "HEAD")).Trim();
        var reader = new CliHistoryReader(new());
        Assert.Equal(head, Assert.Single((await reader.ReadPageAsync(f.Root, head, new(query, 0), f.Token)).Commits).Id);
        await f.GitAsync("switch", "other");
        var other = (await Text(f, "rev-parse", "HEAD")).Trim();
        Assert.Empty((await reader.ReadPageAsync(f.Root, other, new(query, 0), f.Token)).Commits);
        Assert.False(File.Exists(Path.Combine(f.Root, "unexpected")));
    }

    [Fact]
    public async Task WhenTheRootARenameAndAMergeCommitAreRead_ShouldCompareEachWithTheRightParentAndLeaveTheIndexUntouched()
    {
        using var f = await GitFixture.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(f.Root, "old č [x].txt"), "root line\n", f.Token);
        await f.GitAsync("add", "--", "old č [x].txt"); await f.GitAsync("commit", "-m", "Root\n\nDetails body");
        var root = (await Text(f, "rev-parse", "HEAD")).Trim();
        var reader = new CliHistoryReader(new());
        var details = await reader.ReadCommitAsync(f.Root, root, f.Token);
        Assert.Empty(details.Parents); Assert.Contains("Details body", details.Message);
        Assert.Contains((await reader.ReadDiffAsync(f.Root, details, Assert.Single(details.Files), false, f.Token)).Lines, line => line.Text == "+root line");
        await f.GitAsync("switch", "-c", "topic");
        await File.WriteAllTextAsync(Path.Combine(f.Root, "topic.txt"), "topic content\n", f.Token);
        await f.GitAsync("add", "topic.txt"); await f.GitAsync("commit", "-m", "Topic");
        await f.GitAsync("switch", "fixture");
        await f.GitAsync("mv", "--", "old č [x].txt", "renamed č [x].txt");
        await f.GitAsync("commit", "-m", "Rename");
        var renamed = await reader.ReadCommitAsync(f.Root, (await Text(f, "rev-parse", "HEAD")).Trim(), f.Token);
        Assert.Equal("old č [x].txt", Assert.Single(renamed.Files).OriginalPath!.Display);
        await f.GitAsync("merge", "--no-ff", "topic", "-m", "Merge topic");
        var index = await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index"));
        var merge = await reader.ReadCommitAsync(f.Root, (await Text(f, "rev-parse", "HEAD")).Trim(), f.Token);
        Assert.Equal(2, merge.Parents.Count);
        Assert.Equal("topic.txt", Assert.Single(merge.Files).Path.Display);
        Assert.Contains((await reader.ReadDiffAsync(f.Root, merge, merge.Files[0], false, f.Token)).Lines, line => line.Text == "+topic content");
        Assert.Equal(index, await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index")));
    }

    [Fact]
    public async Task WhenHeadIsNoLongerTheGivenCommit_ShouldRefuseThePageAsChangedDuringRead()
    {
        using var f = await GitFixture.CreateAsync();
        var head = await MovedHeadAsync(f);
        var reader = new CliHistoryReader(new());
        Assert.Equal(RepositoryError.ChangedDuringRead, (await Assert.ThrowsAsync<RepositoryException>(() => reader.ReadPageAsync(f.Root, head, new("", 0), f.Token))).Error);
    }

    // A page of every branch read for an unborn HEAD is refused once HEAD has its first commit.
    [Fact]
    public async Task WhenHeadWasUnbornAndNowHasACommit_ShouldRefuseThePageAsChangedDuringRead()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("commit", "--allow-empty", "-m", "First commit");
        var reader = new CliHistoryReader(new());
        Assert.Equal(RepositoryError.ChangedDuringRead, (await Assert.ThrowsAsync<RepositoryException>(() => reader.ReadPageAsync(f.Root, null, new("", 0, AllBranches: true), f.Token))).Error);
    }

    [Fact]
    public async Task WhenTheReadIsCancelled_ShouldEndInCancellationNotInAPage()
    {
        using var f = await GitFixture.CreateAsync();
        var head = await MovedHeadAsync(f);
        var reader = new CliHistoryReader(new());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadPageAsync(f.Root, head, new("search", 0), cancelled.Token));
    }

    [Fact]
    public async Task WhenTheReadIsCancelledWhileGitStreams_ShouldStopGitAndWaitForItToExit()
    {
        using var f = await GitFixture.CreateAsync(); await Import(f, 5000);
        using var cancel = new CancellationTokenSource();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new List<int>();
        var runner = new GitProcessRunner(exited: exited.Add);
        var pending = runner.ReadStreamAsync<int>(f.Root, ["log", "--format=%H%x00%B", "-z", "HEAD", "--"], async (stream, ct) =>
        {
            Assert.NotNull(await new GitRecordStream(stream).ReadAsync(ct));
            ready.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return (0, false);
        }, cancel.Token);
        await ready.Task.WaitAsync(f.Token); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var observed = Assert.Single(exited);
        try { using var process = System.Diagnostics.Process.GetProcessById(observed); Assert.True(process.HasExited); }
        catch (ArgumentException) { /* The OS has already removed this exited process. */ }
    }

    [Fact]
    public async Task WhenHistoryHoldsABinaryFileAndALargeFileThatIsLaterDeleted_ShouldReadTheBinaryAsBinaryAndPreviewTheDeletionWithinBounds()
    {
        using var f = await GitFixture.CreateAsync();
        await File.WriteAllBytesAsync(Path.Combine(f.Root, "binary.bin"), [1, 0, 3], f.Token);
        await File.WriteAllTextAsync(Path.Combine(f.Root, "large.txt"), "old content\n" + new string('x', DiffLimits.ByteLimit), f.Token);
        await f.GitAsync("add", "binary.bin", "large.txt"); await f.GitAsync("commit", "-m", "Contents");
        var reader = new CliHistoryReader(new());
        var first = await reader.ReadCommitAsync(f.Root, (await Text(f, "rev-parse", "HEAD")).Trim(), f.Token);
        Assert.Equal(DiffContentKind.Binary, (await reader.ReadDiffAsync(f.Root, first, first.Files.Single(file => file.Path.Display == "binary.bin"), false, f.Token)).Kind);
        await f.GitAsync("rm", "large.txt"); await f.GitAsync("commit", "-m", "Delete large file");
        var deletion = await reader.ReadCommitAsync(f.Root, (await Text(f, "rev-parse", "HEAD")).Trim(), f.Token);
        var file = Assert.Single(deletion.Files);
        Assert.True((await reader.ReadDiffAsync(f.Root, deletion, file, false, f.Token)).CanPreview);
        var preview = await reader.ReadDiffAsync(f.Root, deletion, file, true, f.Token);
        Assert.InRange(preview.Lines.Count, 1, 200); Assert.Contains(preview.Lines, line => line.Text.Contains("old content", StringComparison.Ordinal));
    }

    // Each commit carries the names Git keeps on it: the branch checked out, other branches, remote-tracking ones and tags,
    // and HEAD itself once it is detached there; Git's stash is not one of them.
    [Fact]
    public async Task WhenBranchesTagsAStashAndADetachedHeadPointAtCommits_ShouldCarryEveryNameButTheStashOnItsCommit()
    {
        using var f = await GitFixture.CreateAsync(initialBranch: "main");
        var first = await f.CommitAsync("First");
        await f.GitAsync("branch", "topic");
        await f.GitAsync("tag", "v1.0");
        await f.GitAsync("update-ref", "refs/remotes/origin/main", first);
        var second = await f.CommitAsync("Second");
        await f.WriteAsync("loose.txt", "stashed\n"); await f.GitAsync("add", "loose.txt"); await f.GitAsync("stash", "push");
        var reader = new CliHistoryReader(new());
        var page = await reader.ReadPageAsync(f.Root, second, new("", 0), f.Token);
        Assert.Equal([("main", CommitRefKind.CurrentBranch)], page.Commits[0].Refs.Select(name => (name.Name, name.Kind)));
        Assert.Equal([("topic", CommitRefKind.Branch), ("origin/main", CommitRefKind.RemoteBranch), ("v1.0", CommitRefKind.Tag)],
            page.Commits[1].Refs.Select(name => (name.Name, name.Kind)));

        await f.GitAsync("checkout", "--detach", first);
        var detached = await reader.ReadPageAsync(f.Root, first, new("", 0), f.Token);
        Assert.Equal([CommitRefKind.DetachedHead, CommitRefKind.Branch, CommitRefKind.RemoteBranch, CommitRefKind.Tag], detached.Commits[0].Refs.Select(name => name.Kind));
    }

    // Settings that change what git log and git show print in a terminal: the page of every branch and a commit's details
    // read the same with each of them as without it. The commit is signed, has a note, a name and a message outside
    // ASCII, and a branch, a remote-tracking branch and an annotated tag on it.
    [Theory]
    [InlineData("log.showSignature", "true")]
    [InlineData("color.ui", "always")]
    [InlineData("log.excludeDecoration", "refs/tags/*")]
    [InlineData("log.initialDecorationSet", "all")]
    [InlineData("i18n.logOutputEncoding", "ISO-8859-2")]
    [InlineData("format.pretty", "oneline")]
    [InlineData("log.abbrevCommit", "true")]
    public async Task WhenTheUserSetsALogOrPrettySetting_ShouldReadThePageAndTheDetailsAsWithoutIt(string key, string value)
    {
        using var f = await GitFixture.CreateAsync(initialBranch: "main");
        await SshSigning.UseAsync(args => f.GitAsync(args), f.Container, f.Token);
        await f.WriteAsync("a.txt", "a\n"); await f.GitAsync("add", "a.txt");
        await f.GitAsync("commit", "-m", "Čvor žute ćelije\n\nTijelo poruke", "--author=Žarko Čučak <zarko@example.invalid>");
        var head = await f.GitAsync("rev-parse", "HEAD");
        await f.GitAsync("branch", "topic");
        await f.GitAsync("tag", "-a", "-m", "Release", "v1.0");
        await f.GitAsync("update-ref", "refs/remotes/origin/main", head);
        await f.GitAsync("notes", "add", "-m", "A note");
        var reader = new CliHistoryReader(new());
        async Task<string> ReadAsync()
        {
            var page = await reader.ReadPageAsync(f.Root, head, new("", 0, AllBranches: true), f.Token);
            var details = await reader.ReadCommitAsync(f.Root, head, f.Token);
            return string.Join("\n", page.Commits.Select(commit => string.Join("|", commit.Id, commit.Author, commit.Email, commit.Date, commit.Subject,
                    string.Join(" ", commit.Parents), string.Join(" ", commit.Refs.Select(name => name.Kind + ":" + name.Name)))))
                + "\n" + string.Join("|", details.Commit.Id, details.Commit.Author, details.Commit.Subject, details.Message, details.Signature,
                    string.Join(" ", details.Files.Select(file => file.Status + ":" + file.Path.Display)));
        }
        var plain = await ReadAsync();

        await f.GitAsync("config", key, value);

        Assert.Equal(plain, await ReadAsync());
        Assert.Contains("Branch:topic RemoteBranch:origin/main Tag:v1.0", plain, StringComparison.Ordinal);
    }

    // Names and messages at the edges of text: an emoji and a right-to-left name, and a carriage return inside the
    // subject, which Git keeps there. The page shows each as Git's %aN and %s have it.
    [Theory]
    [InlineData("Ana 🙂 Anić", "Popravka 🐛 u čvoru\n", "Popravka 🐛 u čvoru")]
    [InlineData("مريم العربية", "إصلاح الخطأ\n", "إصلاح الخطأ")]
    [InlineData("Author", "before\rafter\n", "before\rafter")]
    public async Task WhenAnAuthorOrASubjectIsAtTheEdgeOfText_ShouldShowItAsGitPrintsIt(string author, string message, string subject)
    {
        using var f = await GitFixture.CreateAsync();
        var commit = await OnlyCommitAsync(f, author, message);

        Assert.Equal((author, subject), (commit.Author, commit.Subject));
    }

    [Fact]
    public async Task WhenTheSubjectIsOneCharacterOverWhatARowShows_ShouldClipItWithAnEllipsis()
    {
        using var f = await GitFixture.CreateAsync();
        var commit = await OnlyCommitAsync(f, "Author", new string('s', 1025) + "\n");

        Assert.Equal(("Author", new string('s', 1024) + "…"), (commit.Author, commit.Subject));
    }

    // A commit message larger than a history record may be (2 MB) stops the page; the history then says, in general
    // words, that the history could not be read and that a very large message may be why.
    [Fact]
    public async Task WhenACommitMessageExceedsTwoMegabytes_ShouldRefuseThePageAsTooLarge()
    {
        using var f = await GitFixture.CreateAsync();
        var file = Path.Combine(f.Container, "message.txt");
        await File.WriteAllTextAsync(file, "Huge\n\n" + new string('x', 2 * 1024 * 1024) + "\n");
        await f.GitAsync("commit", "--allow-empty", "-F", file);
        var head = await f.GitAsync("rev-parse", "HEAD");

        await Assert.ThrowsAsync<GitOutputLimitException>(() => new CliHistoryReader(new()).ReadPageAsync(f.Root, head, new(), f.Token));
    }

    // 207 imported commits, read as the three pages of the plain history.
    private static async Task<(string Head, CliHistoryReader Reader, HistoryPage First, HistoryPage Second, HistoryPage Third)> ThreePagesAsync(GitFixture f)
    {
        await Import(f, 207);
        var head = (await Text(f, "rev-parse", "HEAD")).Trim();
        var reader = new CliHistoryReader(new());
        var first = await reader.ReadPageAsync(f.Root, head, new("", 0), f.Token);
        var second = await reader.ReadPageAsync(f.Root, head, new("", 1), f.Token);
        var third = await reader.ReadPageAsync(f.Root, head, new("", 2), f.Token);
        return (head, reader, first, second, third);
    }

    // One commit with the message's exact bytes and the author's name, read as the only commit of the page.
    private static async Task<HistoryCommit> OnlyCommitAsync(GitFixture f, string author, string message)
    {
        var file = Path.Combine(f.Container, "message.txt");
        await File.WriteAllBytesAsync(file, Encoding.UTF8.GetBytes(message));
        await f.GitAsync("commit", "--allow-empty", "--cleanup=verbatim", "-F", file, "--author=" + author + " <author@example.invalid>");
        var head = await f.GitAsync("rev-parse", "HEAD");
        return Assert.Single((await new CliHistoryReader(new()).ReadPageAsync(f.Root, head, new(), f.Token)).Commits);
    }

    // Two imported commits; the head returned is the second, and one more commit has since moved HEAD past it.
    private static async Task<string> MovedHeadAsync(GitFixture f)
    {
        await Import(f, 2);
        var head = (await Text(f, "rev-parse", "HEAD")).Trim();
        await f.GitAsync("commit", "--allow-empty", "-m", "New head");
        return head;
    }

    private static async Task<string> Text(GitFixture f, params string[] args)
    {
        var result = await f.RunAsync(args); Assert.Equal(0, result.ExitCode); return Encoding.UTF8.GetString(result.StandardOutput);
    }
    private static async Task Import(GitFixture f, int count)
    {
        var input = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var identity = i == 0 ? "Special Author č <special@example.invalid>" : "Fixture <fixture@example.invalid>";
            var message = $"Commit {i:D6}\n" + (i == 0 ? "\nDistant body needle\n" : "");
            input.Append($"commit refs/heads/fixture\ncommitter {identity} {1700000000 + i} +0000\ndata {Encoding.UTF8.GetByteCount(message)}\n{message}\n\n");
        }
        input.Append("done\n");
        Assert.Equal(0, (await new GitProcessRunner().RunAsync(f.Root, ["fast-import", "--quiet"], f.Token, standardInput: Encoding.UTF8.GetBytes(input.ToString()))).ExitCode);
    }
}
