using System.Text;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// A commit through the private index in repositories that are not set up as most are: no reflog to find the commit
// by, the real index held open while it is replaced, a hook that prints more than Trag holds, a split index, and a hook
// that stashes under Trag's private index.
public sealed class CommitTests
{
    // The reflog names Trag's own commit; without one, the commit is found by its tree and parents instead.
    [Fact]
    public async Task WhenTheReflogIsDisabled_ShouldStillFindTheCommitByTreeAndPublish()
    {
        using var f = await StagedAsync();
        await f.GitAsync("config", "core.logAllRefUpdates", "false");
        Directory.Delete(Path.Combine(f.Root, ".git", "logs"), true);

        var result = await Commit(f, "Without a reflog");

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        var after = await f.ReadAsync();
        Assert.Equal(result.CommitId, after.Head); Assert.Empty(after.Files);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(f.Root, ".git"), "trag-write-*"));
    }

    // Windows refuses to replace a file another program has open without delete sharing, as an editor or a backup
    // tool may hold the index. The commit is made by then, so it is kept and the private index waits for a review.
    [Fact]
    public async Task WhenTheIndexIsHeldOpenByAnotherProcessDuringPublish_ShouldKeepTheCommitAndReportNotSynchronized()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = await StagedAsync();
        var indexPath = Path.Combine(f.Root, ".git", "index");
        var index = await File.ReadAllBytesAsync(indexPath, f.Token);
        GitWriteResult result;
        using (new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            result = await Commit(f, "Held index");

        Assert.Equal((GitWriteOutcome.Uncertain, CommitOutcomeText.NotSynchronized), (result.Outcome, result.Message));
        Assert.Equal(result.CommitId, await f.GitAsync("rev-parse", "HEAD"));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(f.Root, ".git"), "trag-write-*.index"));
        Assert.Equal(index, await File.ReadAllBytesAsync(indexPath, f.Token));
        Assert.False(File.Exists(indexPath + ".lock"));
    }

    // Trag holds 8 MB of each stream; a hook that prints more is stopped before the commit is made.
    [Fact]
    public async Task WhenAHookPrintsMoreThanTheRunnerHolds_ShouldRejectWithTooMuchOutputAndKeepTheIndex()
    {
        using var f = await StagedAsync();
        var head = await f.GitAsync("rev-parse", "HEAD");
        var index = await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index"), f.Token);
        await Hook(f, "pre-commit", "head -c 9437184 /dev/zero | tr '\\000' x >&2\nexit 0\n");

        var result = await Commit(f, "Loud hook");

        Assert.Equal((GitWriteOutcome.Rejected, WriteText.TooMuchOutput), (result.Outcome, result.Message));
        Assert.Equal(head, await f.GitAsync("rev-parse", "HEAD"));
        Assert.Equal(index, await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index"), f.Token));
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "index.lock")));
    }

    // A hook's own Git commands inherit the reflog action Trag gives the commit, so a hook that moves HEAD in place
    // leaves an entry under that action that names the commit the branch already had. When the commit is refused
    // afterwards, that entry is no commit Trag made.
    [Fact]
    public async Task WhenAHookWritesHeadsReflogAndTheCommitIsRefused_ShouldRejectItWithoutClaimingACommit()
    {
        using var f = await StagedAsync();
        var head = await f.GitAsync("rev-parse", "HEAD");
        await Hook(f, "pre-commit", "git reset --soft HEAD\n");
        await Hook(f, "commit-msg", "exit 1\n");

        var result = await Commit(f, "Refused by commit-msg");

        Assert.Equal((GitWriteOutcome.Rejected, CommitOutcomeText.Rejected), (result.Outcome, result.Message));
        Assert.Null(result.CommitId);
        Assert.Equal(head, await f.GitAsync("rev-parse", "HEAD"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(f.Root, ".git"), "trag-write-*"));
    }

    // A split index keeps most entries in a shared file beside the index; the private copy has to find it, and what it
    // publishes has to leave a repository Git still reads whole.
    [Fact]
    public async Task WhenCoreSplitIndexIsOn_ShouldStageCommitAndUnstageThroughThePrivateIndex()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("config", "core.splitIndex", "true");
        await f.GitAsync("config", "index.version", "4");
        await f.WriteAsync("one.txt", "one\n");
        await f.CommitAsync("Base", "one.txt");
        await f.GitAsync("update-index", "--split-index");
        await f.WriteAsync("one.txt", "one\nmore\n");
        await f.WriteAsync("two.txt", "two\n");

        Assert.Equal(GitWriteOutcome.Succeeded, (await Stage(f, "one.txt", "two.txt")).Outcome);
        Assert.Equal(GitWriteOutcome.Succeeded, (await Commit(f, "Split")).Outcome);
        await f.WriteAsync("two.txt", "two\nagain\n");
        Assert.Equal(GitWriteOutcome.Succeeded, (await Stage(f, "two.txt")).Outcome);
        Assert.Equal(GitWriteOutcome.Succeeded, (await Unstage(f, "two.txt")).Outcome);

        await f.GitAsync("fsck", "--no-progress");
        Assert.Equal("one\nmore\n", await f.GitAsync("show", "HEAD:one.txt") + "\n");
        Assert.Equal("two\n", await f.GitAsync("show", ":two.txt") + "\n");
        Assert.Equal("two.txt", await f.GitAsync("diff", "--name-only"));
    }

    // lint-staged and its kind stash the unstaged changes away while they check the staged ones, and bring them back:
    // under Trag's private index that must still commit the staged change and leave the rest as it was.
    [Fact]
    public async Task WhenAPreCommitHookRunsGitStash_ShouldKeepTheRealIndexAndTheWorkingTree()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("one.txt", "one\n"); await f.WriteAsync("two.txt", "two\n");
        await f.CommitAsync("Base", "one.txt", "two.txt");
        await f.WriteAsync("one.txt", "one\nstaged\n"); await f.GitAsync("add", "one.txt");
        await f.WriteAsync("two.txt", "two\nunstaged\n");
        await Hook(f, "pre-commit", "git stash push --keep-index --quiet && git stash pop --quiet\n");

        var result = await Commit(f, "Checked");

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("one\nstaged\n", await f.GitAsync("show", "HEAD:one.txt") + "\n");
        Assert.Equal("two\n", await f.GitAsync("show", "HEAD:two.txt") + "\n");
        Assert.Equal("two\nunstaged\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "two.txt"), f.Token));
        Assert.Equal("", await f.GitAsync("diff", "--cached", "--name-only"));
        Assert.Equal("", await f.GitAsync("stash", "list"));
    }

    // A commit-msg hook may rewrite the message, as ticket-number hooks do; what Git commits is the rewritten one.
    [Fact]
    public async Task WhenACommitMsgHookRewritesTheMessage_ShouldCommitTheRewrittenOne()
    {
        using var f = await StagedAsync();
        await Hook(f, "commit-msg", "printf 'TICKET-1 ' | cat - \"$1\" > \"$1.new\" && mv \"$1.new\" \"$1\"\n");

        var result = await Commit(f, "Fix the parser");

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("TICKET-1 Fix the parser", await f.GitAsync("log", "-1", "--format=%B"));
    }

    // A refusing commit-msg hook leaves the repository as it was; the message stays the user's to change.
    [Fact]
    public async Task WhenACommitMsgHookRejectsTheMessage_ShouldRefuseTheCommitAndKeepWhatIsStaged()
    {
        using var f = await StagedAsync();
        var head = await f.GitAsync("rev-parse", "HEAD");
        await Hook(f, "commit-msg", "echo 'subject must start with a ticket' >&2\nexit 1\n");

        var result = await Commit(f, "No ticket");

        Assert.Equal((GitWriteOutcome.Rejected, CommitOutcomeText.Rejected), (result.Outcome, result.Message));
        Assert.Contains("subject must start with a ticket", result.Details);
        Assert.Equal((head, "one.txt"), (await f.GitAsync("rev-parse", "HEAD"), await f.GitAsync("diff", "--cached", "--name-only")));
    }

    // prepare-commit-msg runs before Git reads the message Trag gave it; a hook that only adds to it loses nothing.
    [Fact]
    public async Task WhenAPrepareCommitMsgHookAddsALine_ShouldKeepTheMessageAndTheLine()
    {
        using var f = await StagedAsync();
        await Hook(f, "prepare-commit-msg", "printf '\\nPrepared-by: hook\\n' >> \"$1\"\n");

        var result = await Commit(f, "Kept message");

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("Kept message\nPrepared-by: hook", await f.GitAsync("log", "-1", "--format=%B"));
    }

    // Trag commits the message verbatim: a line that starts with # is the user's, not a comment Git strips.
    [Fact]
    public async Task WhenTheMessageHasHashLines_ShouldCommitThemVerbatim()
    {
        using var f = await StagedAsync();

        var result = await Commit(f, "#123 Fix the parser\n\n# not a comment");

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("#123 Fix the parser\n\n# not a comment", await f.GitAsync("log", "-1", "--format=%B"));
    }

    // Trag hands Git the message in UTF-8; a commit encoding the user set for other tools must not label those bytes as
    // another encoding, or every tool that honours the label shows broken letters.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenCommitEncodingIsCp1250_ShouldWriteBytesThatMatchTheEncodingTheCommitNames(bool amend)
    {
        using var f = await StagedAsync();
        await f.GitAsync("config", "i18n.commitEncoding", "cp1250");

        var result = await new GitWriteRouter(new()).ExecuteAsync(new CommitRequest(await f.ReadAsync(), "Čvor đak", Amend: amend), f.Token);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("Čvor đak", await f.GitAsync("log", "-1", "--encoding=UTF-8", "--format=%B"));
    }

    // The new file is staged and committed by its name: the commit holds it under exactly those bytes, and x.txt, changed
    // beside it and what "[x].txt" would match as a glob, stays out.
    [Theory]
    [MemberData(nameof(HostileNameRows.Tracked), MemberType = typeof(HostileNameRows))]
    public async Task WhenAFileHasAHostileName_ShouldCommitItUnderExactlyThatName(string label)
    {
        var name = TestNames.Find(label).Path;
        using var f = await GitFixture.CreateAsync();
        await f.WriteAsync("x.txt", "decoy\n");
        await f.CommitAsync("Decoy", "x.txt");
        await f.WriteAsync("x.txt", "decoy changed\n");
        await f.WriteAsync(name, "hostile\n");

        var staged = await Stage(f, name);
        var committed = await Commit(f, "Hostile name");

        Assert.Equal((GitWriteOutcome.Succeeded, GitWriteOutcome.Succeeded), (staged.Outcome, committed.Outcome));
        Assert.Equal(new[] { name, "x.txt" }.Order(StringComparer.Ordinal), Encoding.UTF8.GetString((await f.RunAsync("ls-tree", "-r", "-z", "--name-only", "HEAD")).StandardOutput)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(("hostile", "decoy"), (await f.GitAsync("show", "HEAD:" + name, "--"), await f.GitAsync("show", "HEAD:x.txt")));
    }

    // husky and lint-staged format the staged files in pre-commit and stage them again: under Trag's private index the
    // commit holds the formatted files, and the index Trag puts in place afterwards holds them too.
    [Fact]
    public async Task WhenAPreCommitHookAddsFormattedFiles_ShouldCommitThemAndPublishTheIndexOnce()
    {
        using var f = await StagedAsync();
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("two.txt", "TWO\n"); await f.GitAsync("add", "two.txt");
        await Hook(f, "pre-commit", "for file in one.txt two.txt; do tr 'a-z' 'A-Z' < $file > $file.new && mv $file.new $file; done\ngit add one.txt two.txt\n");

        var result = await Commit(f, "Formatted");

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal(("ONE\nTWO", "TWO"), (await f.GitAsync("show", "HEAD:one.txt"), await f.GitAsync("show", "HEAD:two.txt")));
        Assert.Equal("", await f.GitAsync("status", "--porcelain"));
    }

    // The user's configuration may clean a message up, start it from a template, add a sign-off or trailers; Trag commits
    // the message verbatim, so what Git keeps is byte for byte what was written.
    [Theory]
    [InlineData("strip")]
    [InlineData("scissors")]
    public async Task WhenTheConfigurationWouldRewriteTheMessage_ShouldCommitItByteForByte(string cleanup)
    {
        using var f = await StagedAsync();
        var template = Path.Combine(f.Container, "template.txt");
        await File.WriteAllTextAsync(template, "Template subject\n\n# template comment\n", f.Token);
        foreach (var (key, value) in new[] { ("commit.cleanup", cleanup), ("core.commentChar", ";"), ("commit.template", template.Replace('\\', '/')),
                     ("commit.verbose", "true"), ("format.signOff", "true"), ("trailer.ifmissing", "add"), ("trailer.sign.key", "Signed-off-by: "),
                     ("trailer.sign.command", "echo Hook <hook@example.invalid>") })
            await f.GitAsync("config", key, value);
        const string message = "Subject  \n\n; not a comment\n# ------------------------ >8 ------------------------\nbelow the scissors\n\n\nlast line  ";

        var result = await Commit(f, message);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        var commit = Encoding.UTF8.GetString((await f.RunAsync("cat-file", "commit", "HEAD")).StandardOutput);
        Assert.Equal(message, commit[(commit.IndexOf("\n\n", StringComparison.Ordinal) + 2)..]);
    }

    // The identity Git stamps can come from a file the repository's configuration includes for its folder alone; Trag
    // shows and commits with what Git uses.
    [Fact]
    public async Task WhenTheIdentityComesFromAnIncludeForTheRepositorysFolder_ShouldShowAndCommitWithIt()
    {
        using var f = await StagedAsync();
        var included = Path.Combine(f.Container, "work.gitconfig");
        await File.WriteAllTextAsync(included, "[user]\n\tname = Work Name\n\temail = work@example.invalid\n", f.Token);
        await f.GitAsync("config", "includeIf.gitdir/i:" + f.Root.Replace('\\', '/') + "/.path", included.Replace('\\', '/'));

        var snapshot = await f.ReadAsync();
        var result = await new GitWriteRouter(new()).ExecuteAsync(new CommitRequest(snapshot, "Work"), f.Token);

        Assert.Equal(new GitIdentity("Work Name", "work@example.invalid"), snapshot.Author);
        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("Work Name <work@example.invalid>", await f.GitAsync("log", "-1", "--format=%an <%ae>"));
    }

    // one.txt committed, then changed and staged.
    private static async Task<GitFixture> StagedAsync()
    {
        var f = await GitFixture.CreateAsync();
        await f.WriteAsync("one.txt", "one\n");
        await f.CommitAsync("Base", "one.txt");
        await f.WriteAsync("one.txt", "one\ntwo\n");
        await f.GitAsync("add", "one.txt");
        return f;
    }

    private static async Task<GitWriteResult> Commit(GitFixture f, string message) =>
        await new GitWriteRouter(new()).ExecuteAsync(new CommitRequest(await f.ReadAsync(), message), f.Token);
    private static async Task<GitWriteResult> Stage(GitFixture f, params string[] paths) =>
        await new GitWriteRouter(new()).ExecuteAsync(new StageRequest(await f.ReadAsync(), Paths(paths)), f.Token);
    private static async Task<GitWriteResult> Unstage(GitFixture f, params string[] paths) =>
        await new GitWriteRouter(new()).ExecuteAsync(new UnstageRequest(await f.ReadAsync(), Paths(paths)), f.Token);
    private static GitPath[] Paths(string[] paths) => paths.Select(path => new GitPath(Encoding.UTF8.GetBytes(path))).ToArray();

    private static async Task Hook(GitFixture f, string name, string body)
    {
        var directory = Directory.CreateDirectory(Path.Combine(f.Container, "empty-hooks")).FullName;
        var path = Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, "#!/bin/sh\n" + body, new UTF8Encoding(false), f.Token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
