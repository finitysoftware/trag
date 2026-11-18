using System.Diagnostics;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// The current branch's commits edited from Trag with real Git, by the plan Trag writes: reordered, squashed, fixed up,
// reworded and dropped, a stop at a conflict continued with the new message still set, an abort, and the refusals that
// keep a rewrite from losing a commit the plan left out or from starting over local changes.
public sealed class RewriteTests
{
    // main: "Base", then "Add b", "Add c" and "Add d", each adding its own file.
    private static async Task<(GitFixture Fixture, string Start, string B, string C, string D)> BranchAsync()
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            await f.WriteAsync("a.txt", "a\n"); var start = await f.CommitAsync("Base");
            await f.WriteAsync("b.txt", "b\n"); var b = await f.CommitAsync("Add b");
            await f.WriteAsync("c.txt", "c\n"); var c = await f.CommitAsync("Add c");
            await f.WriteAsync("d.txt", "d\n"); var d = await f.CommitAsync("Add d");
            return (f, start, b, c, d);
        }
        catch { f.Dispose(); throw; }
    }

    // main: "Base" with x.txt, "Make B" changes its first line to B, "Make C" changes that B to C.
    private static async Task<(GitFixture Fixture, string Start, string B, string C)> StackedAsync()
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            await f.WriteAsync("x.txt", "1\n2\n3\n"); var start = await f.CommitAsync("Base");
            await f.WriteAsync("x.txt", "B\n2\n3\n"); var b = await f.CommitAsync("Make B");
            await f.WriteAsync("x.txt", "C\n2\n3\n"); var c = await f.CommitAsync("Make C");
            return (f, start, b, c);
        }
        catch { f.Dispose(); throw; }
    }

    private static RewriteStep Step(string id, string subject, RewriteAction action = RewriteAction.Pick, string? message = null) =>
        new(id, subject, subject, action, message ?? subject);

    private static Task<GitWriteResult> WriteAsync(GitFixture f, GitWriteRequest request) => new GitWriteRouter(f.Runner).ExecuteAsync(request, f.Token);

    // Every message after the start, oldest first, whole.
    private static async Task<string[]> MessagesAsync(GitFixture f, string start) =>
        [.. (await f.GitAsync("log", "--reverse", "-z", "--format=%B", start + "..HEAD")).Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(message => message.Trim())];

    private static string Messages(GitFixture f) => Path.Combine(f.Root, ".git", "trag-rebase");

    [Fact]
    public async Task WhenTheCommitsAreReordered_ShouldReplayThemInTheNewOrderAndCleanUp()
    {
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;

        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(d, "Add d"), Step(b, "Add b"), Step(c, "Add c")]));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(RewriteText.Rewritten("main"), result.Message);
        Assert.Equal(["Add d", "Add b", "Add c"], await MessagesAsync(f, start));
        var after = await f.ReadAsync();
        Assert.Equal((RepositoryOperation.None, "main"), (after.Operation, after.Branch));
        Assert.False(Directory.Exists(Messages(f)));
    }

    // A file of the plan's folder held open by another program, as an antivirus does, cannot be deleted: the rewrite is
    // still done, and the folder waits for the next one.
    [Fact]
    public async Task WhenThePlansFolderCannotBeDeletedAfterwards_ShouldStillReportTheRewriteDone()
    {
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;
        Directory.CreateDirectory(Messages(f));
        using (File.Open(Path.Combine(Messages(f), "held.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(d, "Add d"), Step(b, "Add b"), Step(c, "Add c")]));

            Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
            Assert.Equal(["Add d", "Add b", "Add c"], await MessagesAsync(f, start));
        }
    }

    // Git refuses a commit left out of the plan when it is told to; a drop is said, so it still goes.
    [Fact]
    public async Task WhenCommitsAreSquashedAndDropped_ShouldJoinThemUnderTheGivenMessageAndLeaveTheDroppedOut()
    {
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;
        await f.GitAsync("config", "rebase.missingCommitsCheck", "error");

        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start,
            [Step(b, "Add b", message: "Add b and c"), Step(c, "Add c", RewriteAction.Squash), Step(d, "Add d", RewriteAction.Drop)]));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["Add b and c"], await MessagesAsync(f, start));
        Assert.True(File.Exists(Path.Combine(f.Root, "c.txt")));
        Assert.False(File.Exists(Path.Combine(f.Root, "d.txt")));
    }

    [Fact]
    public async Task WhenACommitIsFixedUp_ShouldJoinItAndKeepTheFirstOnesMessage()
    {
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;

        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(b, "Add b"), Step(c, "Add c", RewriteAction.Fixup), Step(d, "Add d")]));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["Add b", "Add d"], await MessagesAsync(f, start));
        Assert.True(File.Exists(Path.Combine(f.Root, "c.txt")));
    }

    // The message reaches Git from a file, byte for byte: lines, quotes of both kinds and letters beyond ASCII.
    [Fact]
    public async Task WhenACommitIsReworded_ShouldGiveItTheNewMessageWhole()
    {
        const string Message = "Dodaj „b“ i 'c'\n\nDrugi red \"s navodnicima\"\nTreći red";
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;

        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(b, "Add b", RewriteAction.Reword, Message), Step(c, "Add c"), Step(d, "Add d")]));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal([Message, "Add c", "Add d"], await MessagesAsync(f, start));
    }

    // i18n.logOutputEncoding chooses what Git prints for the user; Trag reads the commits to rewrite in UTF-8 whatever it is,
    // so a squash group's joined message keeps every letter of the messages it joins.
    [Fact]
    public async Task WhenLogOutputEncodingIsCp1250_ShouldKeepEveryMessageByteForByte()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("a.txt", "a\n"); var start = await f.CommitAsync("Base");
        await f.WriteAsync("b.txt", "b\n"); var b = await f.CommitAsync("Dodaj ćevape");
        await f.WriteAsync("c.txt", "c\n"); await f.CommitAsync("Dodaj šljive");
        await f.GitAsync("config", "i18n.logOutputEncoding", "cp1250");

        var steps = (await new CliBranchReader(f.Runner).ReadRewriteAsync(f.Root, b, f.Token))!.Steps;
        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start,
            [steps[0] with { Message = steps[0].Message + "\n\n" + steps[1].Message }, steps[1] with { Action = RewriteAction.Squash }]));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("Dodaj ćevape\n\nDodaj šljive", await f.GitAsync("log", "-1", "--encoding=UTF-8", "--format=%B"));
    }

    // A rewrite's new message reaches Git in UTF-8 from its file; a commit encoding the user set for other tools must not
    // label those bytes as another encoding.
    [Fact]
    public async Task WhenCommitEncodingIsCp1250AndACommitIsReworded_ShouldWriteBytesThatMatchTheEncodingTheCommitNames()
    {
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;
        await f.GitAsync("config", "i18n.commitEncoding", "cp1250");

        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(b, "Add b", RewriteAction.Reword, "Čvor đak"), Step(c, "Add c"), Step(d, "Add d")]));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("Čvor đak", await f.GitAsync("log", "-1", "--encoding=UTF-8", "--format=%B", "HEAD~2"));
    }

    // A rewrite Trag starts writes the stopped commit's message for the rebase view in UTF-8, whatever commit encoding the
    // user set for other tools: the view offers it as it was, and Continue keeps every letter of it.
    [Fact]
    public async Task WhenCommitEncodingIsCp1250AndTheRewriteStopsOnACommitWithANonAsciiMessage_ShouldOfferItAsItWasAndKeepIt()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("x.txt", "1\n2\n3\n"); var start = await f.CommitAsync("Base");
        await f.WriteAsync("x.txt", "B\n2\n3\n"); var b = await f.CommitAsync("Make B");
        await f.WriteAsync("x.txt", "C\n2\n3\n"); var c = await f.CommitAsync("Čvor đak");
        await f.GitAsync("config", "i18n.commitEncoding", "cp1250");
        await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(b, "Make B", RewriteAction.Drop), Step(c, "Čvor đak")]));
        await f.WriteAsync("x.txt", "C\n2\n3\n");
        await f.GitAsync("add", "x.txt");
        var stopped = await f.ReadAsync();
        var offered = MergeMessages.Clean(stopped.Rebase!.Message);

        var continued = await WriteAsync(f, new RebaseContinueRequest(stopped, offered));

        Assert.Equal("Čvor đak", offered);
        Assert.True(continued.Outcome == GitWriteOutcome.Succeeded, continued.Message + continued.Details);
        Assert.Equal("Čvor đak", await f.GitAsync("log", "-1", "--encoding=UTF-8", "--format=%B"));
    }

    // Git writes the files of a fold in the commit encoding of the Git that writes them and commits them in the one of the
    // Git that commits. A fold continued at a conflict and then put back, as when a file stands in its way, is finished
    // by another Git than the one that wrote its message, so every Git Trag starts has to agree on it.
    [Fact]
    public async Task WhenCommitEncodingIsCp1250AndAFoldIsPutBackAfterAContinueInTheSameFold_ShouldKeepTheMessagesLetters()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("x.txt", "1\n2\n3\n"); var start = await f.CommitAsync("Base");
        await f.WriteAsync("x.txt", "B\n2\n3\n"); var b = await f.CommitAsync("Make B");
        await f.WriteAsync("x.txt", "C\n2\n3\n"); var c = await f.CommitAsync("Čvor đak");
        await f.WriteAsync("d.txt", "d\n"); var d = await f.CommitAsync("Add d");
        await f.WriteAsync("e.txt", "e\n"); var e = await f.CommitAsync("Add e");
        await f.GitAsync("config", "i18n.commitEncoding", "cp1250");
        await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start,
            [Step(b, "Make B", RewriteAction.Drop), Step(c, "Čvor đak"), Step(d, "Add d", RewriteAction.Fixup), Step(e, "Add e", RewriteAction.Fixup)]));
        await f.WriteAsync("x.txt", "C\n2\n3\n");
        await f.GitAsync("add", "x.txt");
        await f.WriteAsync("d.txt", "in the way\n");
        await WriteAsync(f, new RebaseContinueRequest(await f.ReadAsync(), "Čvor đak"));
        File.Delete(Path.Combine(f.Root, "d.txt"));
        var putBack = await f.ReadAsync();
        Assert.True(putBack.Rebase!.FoldNotCommitted);

        var continued = await WriteAsync(f, new RebaseContinueRequest(putBack, MergeMessages.Clean(putBack.Rebase.Message)));

        Assert.True(continued.Outcome == GitWriteOutcome.Succeeded, continued.Message + continued.Details);
        Assert.Equal((start, "Čvor đak"), (await f.GitAsync("rev-parse", "HEAD~1"), await f.GitAsync("log", "-1", "--encoding=UTF-8", "--format=%B")));
    }

    // A rewrite stopped in Trag may be continued in a terminal, whose Git has none of the settings Trag gives its own: the
    // exec line that gives a commit its new message still keeps Git from labelling Trag's UTF-8 bytes as the user's commit
    // encoding.
    [Fact]
    public async Task WhenCommitEncodingIsCp1250AndAStoppedRewriteIsContinuedInATerminal_ShouldStillWriteBytesThatMatchTheEncodingTheCommitNames()
    {
        var (f, start, b, c) = await StackedAsync();
        using var _ = f;
        await f.GitAsync("config", "i18n.commitEncoding", "cp1250");
        await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(b, "Make B", RewriteAction.Drop), Step(c, "Make C", RewriteAction.Reword, "Čvor đak")]));
        await f.WriteAsync("x.txt", "C\n2\n3\n");
        await f.GitAsync("add", "x.txt");

        using (var terminal = Process.Start(new ProcessStartInfo(new GitExecutable().Path)
            { WorkingDirectory = f.Root, ArgumentList = { "rebase", "--continue" }, UseShellExecute = false, Environment = { ["GIT_EDITOR"] = ":" } })!)
        {
            await terminal.WaitForExitAsync(f.Token);
            Assert.Equal(0, terminal.ExitCode);
        }

        Assert.Equal("Čvor đak", await f.GitAsync("log", "-1", "--encoding=UTF-8", "--format=%B"));
    }

    // Trag commits every message verbatim, a reworded one too: a line that starts with # is the user's even where
    // commit.cleanup tells Git to strip comments.
    [Fact]
    public async Task WhenARewordedMessageHasHashLinesAndCommitCleanupStrips_ShouldKeepThemVerbatim()
    {
        const string Message = "#123 Add b\n\n# not a comment";
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;
        await f.GitAsync("config", "commit.cleanup", "strip");

        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(b, "Add b", RewriteAction.Reword, Message), Step(c, "Add c"), Step(d, "Add d")]));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal([Message, "Add c", "Add d"], await MessagesAsync(f, start));
    }

    // A message typed in the rewrite dialog ends its lines with Windows' line break; Git gets line feeds alone.
    [Fact]
    public async Task WhenARewordedMessageWasTypedWithWindowsLineBreaks_ShouldGiveItLineFeeds()
    {
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;

        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(b, "Add b", RewriteAction.Reword, "Add b\r\n\r\nWhy."), Step(c, "Add c"), Step(d, "Add d")]));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["Add b\n\nWhy.", "Add c", "Add d"], await MessagesAsync(f, start));
    }

    // A message typed with a line break at its end, as Enter leaves it, ends the commit's message with one line break, as
    // Continue gives a stop's message.
    [Fact]
    public async Task WhenARewordedMessageEndsWithALineBreak_ShouldEndTheCommitsMessageWithOne()
    {
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;

        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(b, "Add b", RewriteAction.Reword, "New\r\n"), Step(c, "Add c"), Step(d, "Add d")]));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.EndsWith("\n\nNew\n", System.Text.Encoding.UTF8.GetString((await f.Runner.RunAsync(f.Root, ["cat-file", "commit", "HEAD~2"], f.Token)).StandardOutput));
    }

    // "Make C" without "Make B" stops at a conflict; once resolved and continued, it still gets the new message, and the
    // plan and messages go with the rebase.
    [Fact]
    public async Task WhenTheRewriteStopsAtAConflict_ShouldSetTheNewMessageOnceItIsContinuedAndCleanUp()
    {
        var (f, start, b, c) = await StackedAsync();
        using var _ = f;

        var started = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(b, "Make B", RewriteAction.Drop), Step(c, "Make C", RewriteAction.Reword, "C alone")]));

        Assert.True(started.Outcome == GitWriteOutcome.NeedsResolution, started.Message + started.Details);
        Assert.True(Directory.Exists(Messages(f)));
        await f.WriteAsync("x.txt", "C\n2\n3\n");
        await f.GitAsync("add", "x.txt");
        var continued = await WriteAsync(f, new RebaseContinueRequest(await f.ReadAsync(), "Make C"));
        Assert.True(continued.Outcome == GitWriteOutcome.Succeeded, continued.Message + continued.Details);
        Assert.Equal(["C alone"], await MessagesAsync(f, start));
        Assert.False(Directory.Exists(Messages(f)));
    }

    // "Make C" folded without "Make B" stops at a conflict; once resolved and continued, the resolution is folded in.
    [Fact]
    public async Task WhenAFoldStopsAtAConflict_ShouldFoldTheResolutionOnceItIsContinued()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("x.txt", "1\n2\n3\n"); var start = await f.CommitAsync("Base");
        await f.WriteAsync("y.txt", "y\n"); var y = await f.CommitAsync("Add y");
        await f.WriteAsync("x.txt", "B\n2\n3\n"); var b = await f.CommitAsync("Make B");
        await f.WriteAsync("x.txt", "C\n2\n3\n"); var c = await f.CommitAsync("Make C");

        var started = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start,
            [Step(y, "Add y"), Step(b, "Make B", RewriteAction.Drop), Step(c, "Make C", RewriteAction.Fixup)]));
        Assert.True(started.Outcome == GitWriteOutcome.NeedsResolution, started.Message + started.Details);
        await f.WriteAsync("x.txt", "C\n2\n3\n");
        await f.GitAsync("add", "x.txt");
        var stopped = await f.ReadAsync();

        var continued = await WriteAsync(f, new RebaseContinueRequest(stopped, MergeMessages.Clean(stopped.Rebase!.Message)));

        Assert.True(continued.Outcome == GitWriteOutcome.Succeeded, continued.Message + continued.Details);
        Assert.Equal((start, "C\n2\n3"), (await f.GitAsync("rev-parse", "HEAD~1"), await f.GitAsync("show", "HEAD:x.txt")));
        Assert.Equal(["Add y"], await MessagesAsync(f, start));
    }

    // A fold Git put back because an untracked file stood in its way has nothing staged: Continue leaves the commit before as
    // it is and lets Git try the fold again, which the file still stops.
    [Fact]
    public async Task WhenAFoldWasPutBackWithNothingStaged_ShouldContinueWithoutChangingTheCommitBefore()
    {
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;
        var todo = Path.Combine(f.Container, "todo");
        await File.WriteAllTextAsync(todo, "pick " + b + "\nedit " + c + "\nfixup " + d + "\n", f.Token);
        await f.GitAsync("-c", "sequence.editor=cp '" + todo.Replace('\\', '/') + "'", "rebase", "-i", start);
        await f.WriteAsync("d.txt", "in the way\n");
        await f.Runner.RunAsync(f.Root, ["rebase", "--continue"], f.Token);
        var stopped = await f.ReadAsync();
        Assert.True(stopped.Rebase!.FoldNotCommitted);

        await WriteAsync(f, new RebaseContinueRequest(stopped, MergeMessages.Clean(stopped.Rebase.Message)));

        Assert.Equal((RepositoryOperation.Rebase, stopped.Head), ((await f.ReadAsync()).Operation, (await f.ReadAsync()).Head));
    }

    // A fold that stops at a conflict and is skipped leaves current-fixups empty until its chain ends, so a later stop
    // without a message file, as a pick an untracked file stood in the way of, is no fold Git put back: Continue and Skip
    // go on as Git's do.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAPickIsPutBackAfterASkippedFold_ShouldContinueOrSkipItAsGitDoes(bool skip)
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("x.txt", "1\n2\n3\n"); var start = await f.CommitAsync("Base");
        await f.WriteAsync("y.txt", "y\n"); var y = await f.CommitAsync("Add y");
        await f.WriteAsync("x.txt", "B\n2\n3\n"); var b = await f.CommitAsync("Make B");
        await f.WriteAsync("x.txt", "C\n2\n3\n"); var c = await f.CommitAsync("Make C");
        await f.WriteAsync("d.txt", "d\n"); var d = await f.CommitAsync("Add d");
        await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start,
            [Step(y, "Add y"), Step(b, "Make B", RewriteAction.Drop), Step(c, "Make C", RewriteAction.Fixup), Step(d, "Add d")]));
        await f.WriteAsync("d.txt", "in the way\n");
        await WriteAsync(f, new RebaseSkipRequest(await f.ReadAsync()));
        File.Delete(Path.Combine(f.Root, "d.txt"));
        var stopped = await f.ReadAsync();

        var result = await WriteAsync(f, skip ? new RebaseSkipRequest(stopped) : new RebaseContinueRequest(stopped, "Add d"));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("Add d\nAdd y\nBase", await f.GitAsync("log", "--format=%s"));
        Assert.False(stopped.Rebase!.FoldNotCommitted);
    }

    // A fixup -C after a squash in a plan the user wrote gets its message a second time when Git folds it again, as a
    // squash does; Continue refuses it once the file that stood in its way is gone.
    [Fact]
    public async Task WhenAFixupWithItsOwnMessageAfterASquashWasPutBack_ShouldRefuseToFoldItAgain()
    {
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;
        var todo = Path.Combine(f.Container, "todo");
        await File.WriteAllTextAsync(todo, "exec echo in the way > d.txt\npick " + b + "\nsquash " + c + "\nfixup -C " + d + "\n", f.Token);
        await f.Runner.RunAsync(f.Root, ["-c", "sequence.editor=cp '" + todo.Replace('\\', '/') + "'", "rebase", "-i", start], f.Token);
        File.Delete(Path.Combine(f.Root, "d.txt"));
        var stopped = await f.ReadAsync();
        Assert.True(stopped.Rebase!.FoldNotCommitted);

        var result = await WriteAsync(f, new RebaseContinueRequest(stopped, MergeMessages.Clean(stopped.Rebase.Message)));

        Assert.Equal((GitWriteOutcome.Rejected, RebaseText.FoldRepeatsMessage), (result.Outcome, result.Message));
        Assert.Equal((RepositoryOperation.Rebase, stopped.Head), ((await f.ReadAsync()).Operation, (await f.ReadAsync()).Head));
    }

    [Fact]
    public async Task WhenTheStoppedRewriteSkipsItsLastCommit_ShouldCleanUp()
    {
        var (f, start, b, c) = await StackedAsync();
        using var _ = f;
        await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(b, "Make B", RewriteAction.Drop), Step(c, "Make C")]));

        var skipped = await WriteAsync(f, new RebaseSkipRequest(await f.ReadAsync()));

        Assert.True(skipped.Outcome == GitWriteOutcome.Succeeded, skipped.Message + skipped.Details);
        Assert.False(Directory.Exists(Messages(f)));
    }

    [Fact]
    public async Task WhenTheStoppedRewriteIsAborted_ShouldPutTheBranchBackWhereItWasAndCleanUp()
    {
        var (f, start, b, c) = await StackedAsync();
        using var _ = f;
        await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(b, "Make B", RewriteAction.Drop), Step(c, "Make C")]));

        var aborted = await WriteAsync(f, new RebaseAbortRequest(await f.ReadAsync()));

        Assert.True(aborted.Outcome == GitWriteOutcome.Succeeded, aborted.Message + aborted.Details);
        Assert.Equal(c, await f.GitAsync("rev-parse", "HEAD"));
        Assert.False(Directory.Exists(Messages(f)));
    }

    // From the first commit there is no base: the history is written anew from the root.
    [Fact]
    public async Task WhenTheRewriteStartsAtTheFirstCommit_ShouldRewriteFromTheRoot()
    {
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;

        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), null,
            [Step(c, "Add c"), Step(start, "Base", RewriteAction.Reword, "The base"), Step(b, "Add b"), Step(d, "Add d")]));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(RewriteText.Rewritten("main"), result.Message);
        Assert.Equal(["Add c", "The base", "Add b", "Add d"], (await f.GitAsync("log", "--reverse", "--format=%s", "HEAD")).Split('\n'));
    }

    // A file named HEAD makes a lone HEAD ambiguous to Git, which then refuses to list the commits the plan has to name.
    [Fact]
    public async Task WhenATopLevelFileIsNamedHead_ShouldStillRewriteFromTheFirstCommit()
    {
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;
        await f.WriteAsync("HEAD", "a file\n"); var e = await f.CommitAsync("Add HEAD", "HEAD");

        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), null,
            [Step(start, "Base"), Step(c, "Add c"), Step(b, "Add b"), Step(d, "Add d"), Step(e, "Add HEAD")]));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["Base", "Add c", "Add b", "Add d", "Add HEAD"], (await f.GitAsync("log", "--reverse", "--format=%s", "HEAD", "--")).Split('\n'));
    }

    // Stopped at a conflict on the way, a rewrite from the root still ends as one once it is continued.
    [Fact]
    public async Task WhenARewriteFromTheFirstCommitStopsAtAConflict_ShouldFinishOnceContinued()
    {
        var (f, start, b, c) = await StackedAsync();
        using var _ = f;

        var started = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), null,
            [Step(start, "Base"), Step(b, "Make B", RewriteAction.Drop), Step(c, "Make C", RewriteAction.Reword, "C alone")]));
        Assert.True(started.Outcome == GitWriteOutcome.NeedsResolution, started.Message + started.Details);
        var stopped = await f.ReadAsync();
        Assert.Equal((true, RebaseText.HistoryStart), (stopped.HasSupportedRebase, stopped.Rebase!.OntoText));
        await f.WriteAsync("x.txt", "C\n2\n3\n");
        await f.GitAsync("add", "x.txt");

        var continued = await WriteAsync(f, new RebaseContinueRequest(await f.ReadAsync(), "Make C"));

        Assert.True(continued.Outcome == GitWriteOutcome.Succeeded, continued.Message + continued.Details);
        Assert.Equal(["Base", "C alone"], (await f.GitAsync("log", "--reverse", "--format=%s", "HEAD")).Split('\n'));
    }

    [Fact]
    public async Task WhenAStoppedRewriteFromTheFirstCommitIsAborted_ShouldPutTheBranchBack()
    {
        var (f, start, b, c) = await StackedAsync();
        using var _ = f;
        await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), null, [Step(start, "Base"), Step(b, "Make B", RewriteAction.Drop), Step(c, "Make C")]));

        var aborted = await WriteAsync(f, new RebaseAbortRequest(await f.ReadAsync()));

        Assert.True(aborted.Outcome == GitWriteOutcome.Succeeded, aborted.Message + aborted.Details);
        Assert.Equal(c, await f.GitAsync("rev-parse", "HEAD"));
    }

    // A commit the plan does not list would be lost without a word, so the plan has to list the branch as it is.
    [Fact]
    public async Task WhenThePlanLeavesACommitOut_ShouldRefuseAndChangeNothing()
    {
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;

        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(c, "Add c"), Step(b, "Add b")]));

        Assert.Equal((GitWriteOutcome.Rejected, RewriteText.PlanStale), (result.Outcome, result.Message));
        Assert.Equal(d, await f.GitAsync("rev-parse", "HEAD"));
    }

    // Git runs the pre-rebase hook before it touches anything; a hook that says no leaves the branch exactly where it was,
    // and the base of a rewrite is in that history all along.
    [Fact]
    public async Task WhenAPreRebaseHookRefusesTheRewrite_ShouldRejectItAndChangeNothing()
    {
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;
        var hooks = Path.Combine(f.Container, "empty-hooks");
        Directory.CreateDirectory(hooks);
        var hook = Path.Combine(hooks, "pre-rebase");
        await File.WriteAllTextAsync(hook, "#!/bin/sh\necho 'rebasing is not allowed here' >&2\nexit 1\n", f.Token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(d, "Add d"), Step(b, "Add b"), Step(c, "Add c")]));

        Assert.NotEqual(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.NotEqual(RewriteText.Rewritten("main"), result.Message);
        Assert.Equal(d, await f.GitAsync("rev-parse", "HEAD"));
        Assert.Equal(["Add b", "Add c", "Add d"], await MessagesAsync(f, start));
        Assert.False(Directory.Exists(Messages(f)));
    }

    [Fact]
    public async Task WhenThereAreLocalChanges_ShouldRefuseToStart()
    {
        var (f, start, b, c, d) = await BranchAsync();
        using var _ = f;
        await f.WriteAsync("a.txt", "changed\n");

        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start, [Step(c, "Add c"), Step(b, "Add b"), Step(d, "Add d")]));

        Assert.Equal((GitWriteOutcome.Rejected, RebaseText.NeedsCleanTree), (result.Outcome, result.Message));
        Assert.Equal(d, await f.GitAsync("rev-parse", "HEAD"));
    }
}
