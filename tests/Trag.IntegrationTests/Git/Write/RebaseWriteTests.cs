using Trag.Domain.Merge;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// A rebase driven from Trag with real Git: started onto a branch, stopped at a conflict, resolved and continued with the
// message the rebase view wrote, a commit skipped, the whole rebase aborted, a rebase with nothing to do, and the refusals
// that keep a rebase from starting over local changes or continuing past an open conflict.
public sealed class RebaseWriteTests
{
    // feature: "Feature changes b" (conflicts with main), "Feature adds y", "Feature adds h"; main changes the same line.
    private static async Task<(GitFixture Fixture, string Main)> Diverged(bool conflicting = true)
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            await f.WriteAsync("f.txt", "a\nb\nc\n"); await f.WriteAsync("g.txt", "x\n"); await f.CommitAsync("Base");
            await f.GitAsync("switch", "-c", "feature");
            await f.WriteAsync("f.txt", "a\nB-feature\nc\n"); await f.CommitAsync("Feature changes b");
            await f.WriteAsync("g.txt", "x\ny\n"); await f.CommitAsync("Feature adds y");
            await f.WriteAsync("h.txt", "h\n"); await f.CommitAsync("Feature adds h");
            await f.GitAsync("switch", "main");
            if (conflicting) { await f.WriteAsync("f.txt", "a\nB-main\nc\n"); await f.CommitAsync("Main changes b"); }
            else { await f.WriteAsync("m.txt", "m\n"); await f.CommitAsync("Main adds m"); }
            var main = (await f.GitAsync("rev-parse", "HEAD")).Trim();
            await f.GitAsync("switch", "feature");
            return (f, main);
        }
        catch { f.Dispose(); throw; }
    }

    private static Task<GitWriteResult> Write(GitFixture f, GitWriteRequest request) => new GitWriteRouter(f.Runner).ExecuteAsync(request, f.Token);

    private static async Task<IReadOnlyList<string>> Subjects(GitFixture f, int count) =>
        (await f.GitAsync("log", "--format=%s", "-" + count)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task WhenTheRebaseStopsAtAConflict_ShouldRefuseToContinueUntilItIsResolvedThenCommitItWithTheMessageTragWrote()
    {
        var (f, main) = await Diverged();
        using var _ = f;
        var started = await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main));
        Assert.True(started.Outcome == GitWriteOutcome.NeedsResolution, started.Message + started.Details);
        Assert.Equal("The rebase stopped at commit 1 of 3. Resolve its conflicts, then continue.", started.Message);

        var stopped = await f.ReadAsync();
        Assert.True(stopped.HasSupportedRebase);
        // A stop at a conflict is no fold Git put back, whatever the plan's first line is.
        Assert.False(stopped.Rebase!.FoldPlanChanged);
        var refused = await Write(f, new RebaseContinueRequest(stopped, "Feature changes b"));
        Assert.Equal(GitWriteOutcome.Rejected, refused.Outcome);
        Assert.StartsWith("Resolve and mark every conflicted file", refused.Message);

        // Your side of a rebase is Git's theirs.
        var yours = await Write(f, new ConflictSideRequest(stopped, Snapshots.Path("f.txt"), ConflictChoice.Theirs));
        Assert.True(yours.Outcome == GitWriteOutcome.Succeeded, yours.Message + yours.Details);
        var continued = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), "Feature changes b, kept over main"));
        Assert.True(continued.Outcome == GitWriteOutcome.Succeeded, continued.Message + continued.Details);
        Assert.Equal("Rebased feature onto main.", continued.Message);

        var after = await f.ReadAsync();
        Assert.Equal((RepositoryOperation.None, "feature"), (after.Operation, after.Branch));
        Assert.Equal(["Feature adds h", "Feature adds y", "Feature changes b, kept over main", "Main changes b"], await Subjects(f, 4));
        Assert.Equal("a\nB-feature\nc\n", File.ReadAllText(Path.Combine(f.Root, "f.txt")));
    }

    // The rebase view's message comes with Windows line endings from the editor; under commit.cleanup=verbatim Git keeps
    // every byte it is given, so the message goes to Git with Git's own line endings.
    [Fact]
    public async Task WhenCommitCleanupIsVerbatimAndTheMessageHasWindowsLineEndings_ShouldCommitNoCarriageReturn()
    {
        var (f, main) = await Diverged();
        using var _ = f;
        await f.GitAsync("config", "commit.cleanup", "verbatim");
        await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main));
        await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("f.txt"), ConflictChoice.Theirs));

        var continued = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), "Feature changes b\r\n\r\nKept over main.\r\n"));

        Assert.True(continued.Outcome == GitWriteOutcome.Succeeded, continued.Message + continued.Details);
        Assert.Equal("Feature changes b\n\nKept over main.", await f.GitAsync("show", "-s", "--format=%B", "HEAD~2"));
    }

    // Trag hands Git the message in UTF-8, at a conflict through Git's message file and at an edit stop through its amend;
    // a commit encoding the user set for other tools must not label those bytes as another encoding.
    [Fact]
    public async Task WhenCommitEncodingIsCp1250AndAStoppedCommitGetsANewMessage_ShouldWriteBytesThatMatchTheEncodingTheCommitNames()
    {
        var (f, main) = await Diverged();
        using var _ = f;
        await f.GitAsync("config", "i18n.commitEncoding", "cp1250");
        await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main));
        await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("f.txt"), ConflictChoice.Theirs));

        var continued = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), "Čvor đak"));

        Assert.True(continued.Outcome == GitWriteOutcome.Succeeded, continued.Message + continued.Details);
        Assert.Equal("Čvor đak", await f.GitAsync("log", "-1", "--encoding=UTF-8", "--format=%B", "HEAD~2"));
    }

    [Fact]
    public async Task WhenCommitEncodingIsCp1250AndAnEditStopGetsANewMessage_ShouldWriteBytesThatMatchTheEncodingTheCommitNames()
    {
        var (f, _) = await Diverged(conflicting: false);
        using var _ = f;
        await f.GitAsync("-c", "rebase.abbreviateCommands=false", "-c", "sequence.editor=sed -i 1s/^pick/edit/", "rebase", "-i", "main");
        await f.GitAsync("config", "i18n.commitEncoding", "cp1250");

        var result = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), "Čvor đak"));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("Čvor đak", await f.GitAsync("log", "-1", "--encoding=UTF-8", "--format=%B", "HEAD~2"));
    }

    // A rebase Trag starts writes the stopped commit's message for the rebase view in UTF-8, whatever commit encoding the
    // user set for other tools: the view offers it as it was, and Continue keeps every letter of it.
    [Fact]
    public async Task WhenCommitEncodingIsCp1250AndTheRebaseStopsOnACommitWithANonAsciiMessage_ShouldOfferItAsItWasAndKeepIt()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("f.txt", "a\nb\nc\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("f.txt", "a\nB-feature\nc\n"); await f.CommitAsync("Čvor đak");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("f.txt", "a\nB-main\nc\n"); var main = await f.CommitAsync("Main changes b");
        await f.GitAsync("switch", "feature");
        await f.GitAsync("config", "i18n.commitEncoding", "cp1250");
        await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main));
        var offered = MergeMessages.Clean((await f.ReadAsync()).Rebase!.Message);
        await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("f.txt"), ConflictChoice.Theirs));

        var continued = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), offered));

        Assert.Equal("Čvor đak", offered);
        Assert.True(continued.Outcome == GitWriteOutcome.Succeeded, continued.Message + continued.Details);
        Assert.Equal("Čvor đak", await f.GitAsync("log", "-1", "--encoding=UTF-8", "--format=%B"));
    }

    // Git writes the subjects of a rebase's plan in i18n.logOutputEncoding when the rebase starts: one Trag starts shows
    // them as they are under another.
    [Fact]
    public async Task WhenLogOutputEncodingIsCp1250AndTheRebaseStopsOnACommitWithANonAsciiSubject_ShouldShowItInThePlan()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("f.txt", "a\nb\nc\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("f.txt", "a\nB-feature\nc\n"); await f.CommitAsync("Čvor đak");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("f.txt", "a\nB-main\nc\n"); var main = await f.CommitAsync("Main changes b");
        await f.GitAsync("switch", "feature");
        await f.GitAsync("config", "i18n.logOutputEncoding", "cp1250");

        await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main));

        Assert.Equal("Čvor đak", (await f.ReadAsync()).Rebase!.Current!.Subject);
    }

    // A rebase started in the terminal stops for an edit: the commit is made, and the rebase view's message, edited, is
    // what that commit ends up with when Trag continues.
    [Fact]
    public async Task WhenARebaseStartedOutsideStopsForEdit_ShouldContinueWithTheEditedMessage()
    {
        var (f, _) = await Diverged(conflicting: false);
        using var _ = f;
        await f.GitAsync("-c", "rebase.abbreviateCommands=false", "-c", "sequence.editor=sed -i 1s/^pick/edit/", "rebase", "-i", "main");
        var stopped = await f.ReadAsync();
        Assert.True(stopped.Rebase!.StoppedForEdit);

        var result = await Write(f, new RebaseContinueRequest(stopped, "Feature changes b, reworded"));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["Feature adds h", "Feature adds y", "Feature changes b, reworded", "Main adds m"], await Subjects(f, 4));
    }

    // A hook that refuses the amend of an edited message keeps the stop as it was, so Continue tried again gives the commit
    // that message.
    [Fact]
    public async Task WhenAHookRefusesTheEditedMessageOnce_ShouldGiveItToTheCommitOnTheNextContinue()
    {
        var (f, _) = await Diverged(conflicting: false);
        using var _ = f;
        await f.GitAsync("-c", "rebase.abbreviateCommands=false", "-c", "sequence.editor=sed -i 1s/^pick/edit/", "rebase", "-i", "main");
        var hooks = Path.Combine(f.Container, "hooks");
        Directory.CreateDirectory(hooks);
        var refusedOnce = Path.Combine(f.Container, "refused-once").Replace('\\', '/');
        await ShellScript.WriteAsync(Path.Combine(hooks, "pre-commit"), "#!/bin/sh\n[ -f '" + refusedOnce + "' ] && exit 0\ntouch '" + refusedOnce + "'\nexit 1\n", f.Token);
        await f.GitAsync("config", "core.hooksPath", hooks.Replace('\\', '/'));
        var refused = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), "Feature changes b, reworded"));

        var result = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), "Feature changes b, reworded"));

        Assert.Equal(GitWriteOutcome.Rejected, refused.Outcome);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["Feature adds h", "Feature adds y", "Feature changes b, reworded", "Main adds m"], await Subjects(f, 4));
    }

    // Git continues an edit stop only once every change is staged, and refuses for good once the commit it stopped at has
    // moved beside a change not staged. Continue refuses before the amend gives the commit a new message, and with the
    // message as it was, where Git would speak of conflicts; once the change is staged the commit gets it with the message.
    [Theory]
    [InlineData("Feature changes b, reworded")]
    [InlineData("Feature changes b")]
    public async Task WhenAnEditStopIsContinuedBesideAChangeNotStaged_ShouldRefuseBeforeTheAmendAndTakeItOnceItIsStaged(string message)
    {
        var (f, _) = await Diverged(conflicting: false);
        using var _ = f;
        await f.GitAsync("-c", "rebase.abbreviateCommands=false", "-c", "sequence.editor=sed -i 1s/^pick/edit/", "rebase", "-i", "main");
        await f.WriteAsync("f.txt", "a\nB-feature\nc\nedited at the stop\n");
        var stopped = await f.ReadAsync();

        var refused = await Write(f, new RebaseContinueRequest(stopped, message));

        Assert.Equal((GitWriteOutcome.Rejected, RebaseText.EditNeedsStaged), (refused.Outcome, refused.Message));
        Assert.Equal(stopped.Head, (await f.ReadAsync()).Head);
        await f.GitAsync("add", "f.txt");
        var continued = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), message));
        Assert.True(continued.Outcome == GitWriteOutcome.Succeeded, continued.Message + continued.Details);
        Assert.Equal(["Feature adds h", "Feature adds y", message, "Main adds m"], await Subjects(f, 4));
        Assert.Equal("a\nB-feature\nc\nedited at the stop", await f.GitAsync("show", "HEAD~2:f.txt"));
    }

    // At an ordinary edit stop Git keeps its amend file, and its own continue amends what is staged into the commit without
    // the user's commit hooks, as it does in a terminal. With the stop's message as it was, Continue leaves that amend to
    // Git, so a commit-msg hook that refuses every message does not stop the rebase there.
    [Fact]
    public async Task WhenAnEditStopIsContinuedWithItsMessageAndAStagedChange_ShouldLeaveTheAmendToGit()
    {
        var (f, _) = await Diverged(conflicting: false);
        using var _ = f;
        await f.GitAsync("-c", "rebase.abbreviateCommands=false", "-c", "sequence.editor=sed -i 1s/^pick/edit/", "rebase", "-i", "main");
        await f.WriteAsync("f.txt", "a\nB-feature\nc\nstaged at the stop\n");
        await f.GitAsync("add", "f.txt");
        var hooks = Path.Combine(f.Container, "hooks");
        Directory.CreateDirectory(hooks);
        await ShellScript.WriteAsync(Path.Combine(hooks, "commit-msg"), "#!/bin/sh\nexit 1\n", f.Token);
        await f.GitAsync("config", "core.hooksPath", hooks.Replace('\\', '/'));
        var stopped = await f.ReadAsync();

        var result = await Write(f, new RebaseContinueRequest(stopped, MergeMessages.Clean(stopped.Rebase!.Message)));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["Feature adds h", "Feature adds y", "Feature changes b", "Main adds m"], await Subjects(f, 4));
        Assert.Equal("a\nB-feature\nc\nstaged at the stop", await f.GitAsync("show", "HEAD~2:f.txt"));
    }

    // A hook may change a tracked file while Continue amends the commit and leave that change not staged, beside which Git
    // refuses to go on, and for good once the commit has moved. Continue says so before Git goes on and keeps the stop's
    // message, so once the change is staged Continue amends it in with the message and goes on.
    [Fact]
    public async Task WhenAHookChangesATrackedFileWhileContinueAmendsAnEditStop_ShouldRefuseToGoOnUntilItIsStaged()
    {
        var (f, _) = await Diverged(conflicting: false);
        using var _ = f;
        await f.GitAsync("-c", "rebase.abbreviateCommands=false", "-c", "sequence.editor=sed -i 1s/^pick/edit/", "rebase", "-i", "main");
        var hooks = Path.Combine(f.Container, "hooks");
        Directory.CreateDirectory(hooks);
        await ShellScript.WriteAsync(Path.Combine(hooks, "pre-commit"), "#!/bin/sh\necho stamped > m.txt\n", f.Token);
        await f.GitAsync("config", "core.hooksPath", hooks.Replace('\\', '/'));
        var stopped = await f.ReadAsync();

        var refused = await Write(f, new RebaseContinueRequest(stopped, "Feature changes b, reworded"));

        Assert.Equal((GitWriteOutcome.Rejected, RebaseText.EditChangedWhileAmended), (refused.Outcome, refused.Message));
        await f.GitAsync("add", "m.txt");
        var continued = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), "Feature changes b, reworded"));
        Assert.True(continued.Outcome == GitWriteOutcome.Succeeded, continued.Message + continued.Details);
        Assert.Equal(["Feature adds h", "Feature adds y", "Feature changes b, reworded", "Main adds m"], await Subjects(f, 4));
        Assert.Equal("stamped", await f.GitAsync("show", "HEAD~2:m.txt"));
    }

    // Git goes on beside a submodule whose files changed, so the check after the amend leaves submodules out, as Git's does.
    [Fact]
    public async Task WhenAnEditStopGetsANewMessageBesideASubmoduleWithChangesOfItsOwn_ShouldContinue()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        var source = Path.Combine(f.Container, "sub-source");
        await f.GitAtAsync(f.Container, "init", source);
        await File.WriteAllTextAsync(Path.Combine(source, "s.txt"), "s\n", f.Token);
        await f.GitAtAsync(source, "add", "s.txt");
        await f.GitAtAsync(source, "-c", "user.name=Sub", "-c", "user.email=sub@example.invalid", "commit", "-m", "Sub");
        await f.GitAsync("-c", "protocol.file.allow=always", "submodule", "add", "--", new Uri(source).AbsoluteUri, "sub");
        await f.WriteAsync("f.txt", "a\nb\nc\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("f.txt", "a\nB-feature\nc\n"); await f.CommitAsync("Feature changes b");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("m.txt", "m\n"); await f.CommitAsync("Main adds m");
        await f.GitAsync("switch", "feature");
        await f.GitAsync("-c", "rebase.abbreviateCommands=false", "-c", "sequence.editor=sed -i 1s/^pick/edit/", "rebase", "-i", "main");
        await File.WriteAllTextAsync(Path.Combine(f.Root, "sub", "s.txt"), "built\n", f.Token);

        var result = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), "Feature changes b, reworded"));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["Feature changes b, reworded", "Main adds m"], await Subjects(f, 2));
    }

    // feature's first commit reworded in a rebase onto main started in a terminal, with a commit-msg hook that refuses the
    // first message it is given. main has moved, so Git replays the commit and makes it before it asks for the message. The
    // plan names the command as written there: "r" is what git rebase --edit-todo leaves under rebase.abbreviateCommands.
    private static async Task RewordRefusedAsync(GitFixture f, string command)
    {
        var hooks = Path.Combine(f.Container, "hooks");
        Directory.CreateDirectory(hooks);
        var refusedOnce = Path.Combine(f.Container, "refused-once").Replace('\\', '/');
        await ShellScript.WriteAsync(Path.Combine(hooks, "commit-msg"), "#!/bin/sh\n[ -f '" + refusedOnce + "' ] && exit 0\ntouch '" + refusedOnce + "'\nexit 1\n", f.Token);
        await f.GitAsync("config", "core.hooksPath", hooks.Replace('\\', '/'));
        await f.GitAsync("-c", "sequence.editor=sed -i 1ibreak", "rebase", "-i", "main");
        await f.GitAsync("-c", "rebase.abbreviateCommands=true", "-c", "sequence.editor=sed -i 1s/^p/" + command + "/", "rebase", "--edit-todo");
        await f.RunAsync("-c", "core.editor=true", "rebase", "--continue");
    }

    // A reword a hook refused after Git replayed it is made already, as at an edit stop, but Git keeps no amend file for it:
    // its own continue leaves the old message and commits what is staged as a commit of its own. Continue gives the commit
    // the message and what is staged, as at an edit stop.
    [Theory]
    [InlineData("reword", "PROJ-1 Feature changes b", false)]
    [InlineData("r", "PROJ-1 Feature changes b", false)]
    [InlineData("reword", "PROJ-1 Feature changes b", true)]
    [InlineData("reword", "Feature changes b", true)]
    public async Task WhenAHookRefusedARewordGitReplayed_ShouldGiveTheCommitTheMessageAndWhatIsStaged(string command, string message, bool staged)
    {
        var (f, _) = await Diverged(conflicting: false);
        using var _ = f;
        await RewordRefusedAsync(f, command);
        if (staged)
        {
            await f.WriteAsync("f.txt", "a\nB-feature\nc\nstaged at the stop\n");
            await f.GitAsync("add", "f.txt");
        }

        var result = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), message));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["Feature adds h", "Feature adds y", message, "Main adds m"], await Subjects(f, 4));
        Assert.Equal(staged ? "a\nB-feature\nc\nstaged at the stop" : "a\nB-feature\nc", await f.GitAsync("show", "HEAD~2:f.txt"));
    }

    // A reword that stops at a conflict and is reset in a terminal leaves no amend file, no MERGE_MSG and no conflict, as a
    // reword a hook refused does, but HEAD is the commit before it: Continue gives that commit no message, and Git goes on
    // without the commit that was reset, as its own continue does.
    [Fact]
    public async Task WhenARewordStoppedAtAConflictIsResetHard_ShouldNotGiveTheCommitBeforeItTheMessage()
    {
        var (f, _) = await Diverged();
        using var _ = f;
        await f.RunAsync("-c", "core.editor=true", "-c", "sequence.editor=sed -i 1s/^pick/reword/", "rebase", "-i", "main");
        await f.GitAsync("reset", "--hard");

        var result = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), "PROJ-1 Feature changes b"));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["Feature adds h", "Feature adds y", "Main changes b", "Base"], await Subjects(f, 4));
    }

    // A message typed with line breaks at its end, as an editor leaves them, ends the edited commit's message with one line
    // break, as a rewrite's message does.
    [Fact]
    public async Task WhenAnEditStopsMessageEndsWithLineBreaks_ShouldEndTheCommitsMessageWithOne()
    {
        var (f, _) = await Diverged(conflicting: false);
        using var _ = f;
        await f.GitAsync("-c", "rebase.abbreviateCommands=false", "-c", "sequence.editor=sed -i 1s/^pick/edit/", "rebase", "-i", "main");

        var result = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), "New\r\n\r\n"));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.EndsWith("\n\nNew\n", System.Text.Encoding.UTF8.GetString((await f.Runner.RunAsync(f.Root, ["cat-file", "commit", "HEAD~2"], f.Token)).StandardOutput));
    }

    // A break stops between commits and an exec that fails stops after its commit: Trag continues either and no commit
    // gets the message the rebase view held for the stop.
    [Theory]
    [InlineData("1a break")]
    [InlineData("1a exec false")]
    public async Task WhenARebaseStartedOutsideStopsAtABreakOrAFailingExec_ShouldContinueAndLeaveEveryMessage(string line)
    {
        var (f, _) = await Diverged(conflicting: false);
        using var _ = f;
        await f.RunAsync("-c", "sequence.editor=sed -i '" + line + "'", "rebase", "-i", "main");
        var stopped = await f.ReadAsync();
        Assert.Equal(RepositoryOperation.Rebase, stopped.Operation);

        var result = await Write(f, new RebaseContinueRequest(stopped, "Message the view held"));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["Feature adds h", "Feature adds y", "Feature changes b", "Main adds m"], await Subjects(f, 4));
    }

    // rebase.updateRefs would move every branch that points into the rebased commits; Trag's rebase moves its own alone.
    [Fact]
    public async Task WhenRebaseUpdateRefsIsOn_ShouldNotMoveOtherBranches()
    {
        var (f, main) = await Diverged(conflicting: false);
        using var _ = f;
        await f.GitAsync("config", "rebase.updateRefs", "true");
        await f.GitAsync("branch", "marker", "HEAD~1");
        var marker = await f.GitAsync("rev-parse", "marker");

        var result = await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(marker, await f.GitAsync("rev-parse", "marker"));
    }

    // rebase.rebaseMerges would make the branch's merge commits again; Trag's rebase leaves them out, as its preview says.
    [Fact]
    public async Task WhenRebaseMergesIsOnInConfig_ShouldLeaveMergesOutAsThePreviewSaid()
    {
        var (f, main) = await Diverged(conflicting: false);
        using var _ = f;
        await f.GitAsync("switch", "-c", "side", "HEAD~1");
        await f.WriteAsync("s.txt", "s\n"); await f.CommitAsync("Side adds s");
        await f.GitAsync("switch", "feature");
        await f.GitAsync("merge", "--no-ff", "-m", "Merge side", "side");
        await f.GitAsync("config", "rebase.rebaseMerges", "true");

        var result = await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("", await f.GitAsync("rev-list", "--min-parents=2", main + "..HEAD"));
    }

    // rebase.autoSquash would fold a "fixup!" commit into its target; Trag's rebase replays every commit as it is.
    [Fact]
    public async Task WhenRebaseAutosquashIsOn_ShouldNotSquash()
    {
        var (f, main) = await Diverged(conflicting: false);
        using var _ = f;
        await f.GitAsync("config", "rebase.autoSquash", "true");
        await f.WriteAsync("h.txt", "h fixed\n"); await f.CommitAsync("fixup! Feature adds h");

        var result = await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(["fixup! Feature adds h", "Feature adds h", "Feature adds y", "Feature changes b", "Main adds m"], await Subjects(f, 5));
    }

    // A line of the message that starts with '#', an issue number, is the user's text: Git commits it as it was written.
    [Fact]
    public async Task WhenTheContinuedMessageHasALineStartingWithAHash_ShouldCommitItAsItWasWritten()
    {
        var (f, main) = await Diverged();
        using var _ = f;
        Assert.Equal(GitWriteOutcome.NeedsResolution, (await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main))).Outcome);
        Assert.Equal(GitWriteOutcome.Succeeded, (await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("f.txt"), ConflictChoice.Theirs))).Outcome);

        var continued = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), "#123 Feature changes b\n\n#124 kept over main"));

        Assert.True(continued.Outcome == GitWriteOutcome.Succeeded, continued.Message + continued.Details);
        Assert.Equal("#123 Feature changes b\n\n#124 kept over main", await f.GitAsync("log", "-1", "--format=%B", "HEAD~2"));
    }

    // The stopped commit's message is edited in Trag's editor, which ends a typed line with Windows' line break.
    [Fact]
    public async Task WhenTheContinuedMessageWasTypedWithWindowsLineBreaks_ShouldCommitItWithLineFeeds()
    {
        var (f, main) = await Diverged();
        using var _ = f;
        Assert.Equal(GitWriteOutcome.NeedsResolution, (await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main))).Outcome);
        var stopped = await f.ReadAsync();
        Assert.Equal(GitWriteOutcome.Succeeded, (await Write(f, new ConflictSideRequest(stopped, Snapshots.Path("f.txt"), ConflictChoice.Theirs))).Outcome);

        var continued = await Write(f, new RebaseContinueRequest(await f.ReadAsync(), "Feature changes b\r\n\r\nKept over main."));

        Assert.True(continued.Outcome == GitWriteOutcome.Succeeded, continued.Message + continued.Details);
        Assert.Equal("Feature changes b\n\nKept over main.", await f.GitAsync("log", "-1", "--format=%B", "HEAD~2"));
    }

    [Fact]
    public async Task WhenTheStoppedCommitIsSkipped_ShouldLeaveItOutOfTheRebasedBranch()
    {
        var (f, main) = await Diverged();
        using var _ = f;
        await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main));
        var skipped = await Write(f, new RebaseSkipRequest(await f.ReadAsync()));
        Assert.True(skipped.Outcome == GitWriteOutcome.Succeeded, skipped.Message + skipped.Details);
        Assert.Equal(["Feature adds h", "Feature adds y", "Main changes b"], await Subjects(f, 3));
    }

    [Fact]
    public async Task WhenTheRebaseIsAborted_ShouldPutTheBranchBackWhereItWas()
    {
        var (f, main) = await Diverged();
        using var _ = f;
        var before = (await f.ReadAsync()).Head;
        await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main));
        var aborted = await Write(f, new RebaseAbortRequest(await f.ReadAsync()));
        Assert.True(aborted.Outcome == GitWriteOutcome.Succeeded, aborted.Message + aborted.Details);
        Assert.Equal("Rebase aborted. feature is back where it was before the rebase.", aborted.Message);
        var after = await f.ReadAsync();
        Assert.Equal((RepositoryOperation.None, "feature", before), (after.Operation, after.Branch, after.Head));
    }

    [Fact]
    public async Task WhenTheRebaseIsClean_ShouldReplayEveryCommitAndHaveNothingToDoTheSecondTime()
    {
        var (f, main) = await Diverged(conflicting: false);
        using var _ = f;
        var rebased = await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main));
        Assert.True(rebased.Outcome == GitWriteOutcome.Succeeded, rebased.Message + rebased.Details);
        Assert.Equal(["Feature adds h", "Feature adds y", "Feature changes b", "Main adds m"], await Subjects(f, 4));
        var again = await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main));
        Assert.Equal((GitWriteOutcome.Succeeded, "feature already contains main. Nothing was rebased."), (again.Outcome, again.Message));
    }

    // Without a common commit Git replays every commit of the branch onto the other, as many as the preview counted.
    [Fact]
    public async Task WhenTheHistoriesAreUnrelated_ShouldStartOrRefuseAsThePreviewSaid()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "a\n"); var main = await f.CommitAsync("Base");
        await f.GitAsync("switch", "--orphan", "other");
        await f.WriteAsync("other.txt", "other\n"); await f.CommitAsync("Other root", "other.txt");
        await f.WriteAsync("other.txt", "other, more\n"); await f.CommitAsync("Other more", "other.txt");
        var preview = await new Trag.Infrastructure.Git.Read.CliBranchReader(f.Runner).PreviewRebaseAsync(f.Root, "refs/heads/main", f.Token);

        var result = await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal((2, main), (preview.ReplayedCount, await f.GitAsync("rev-parse", "HEAD~" + preview.ReplayedCount)));
        Assert.Equal(["Other more", "Other root", "Base"], await Subjects(f, 3));
    }

    [Fact]
    public async Task WhenThereAreLocalChanges_ShouldRefuseToStartTheRebase()
    {
        var (f, main) = await Diverged();
        using var _ = f;
        await f.WriteAsync("g.txt", "x\ny\nlocal\n");
        var refused = await Write(f, new RebaseRequest(await f.ReadAsync(), "main", main));
        Assert.Equal(GitWriteOutcome.Rejected, refused.Outcome);
        Assert.Equal(RepositoryOperation.None, (await f.ReadAsync()).Operation);
    }
}
