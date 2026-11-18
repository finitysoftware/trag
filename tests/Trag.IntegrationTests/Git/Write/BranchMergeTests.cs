using Trag.Application.Ports.Git;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// A branch the user chose, merged into the current one with real Git: a fast-forward, a merge commit asked for or needed,
// the merge a conflict leaves for the user, and the two states that change nothing (a dirty tree, a commit already there).
public sealed class BranchMergeTests
{
    // main has one commit; feature adds two on top of it and main stays where it was unless ownCommit adds one there too.
    private static async Task<(GitFixture Fixture, string Feature)> Branches(bool ownCommit = false, bool conflict = false)
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            await f.WriteAsync("shared.txt", "base\n"); await f.CommitAsync("Base");
            await f.GitAsync("switch", "-c", "feature");
            await f.WriteAsync("feature.txt", "one\n"); await f.CommitAsync("Feature one");
            await f.WriteAsync("shared.txt", conflict ? "feature side\n" : "base\n"); await f.WriteAsync("feature.txt", "two\n");
            var feature = await f.CommitAsync("Feature two");
            await f.GitAsync("switch", "main");
            if (ownCommit || conflict)
            {
                await f.WriteAsync(conflict ? "shared.txt" : "main.txt", "main side\n");
                await f.CommitAsync("Main own");
            }
            return (f, feature);
        }
        catch { f.Dispose(); throw; }
    }

    private static async Task<GitWriteResult> Merge(GitFixture f, string feature, bool noFastForward = false, string message = "Merge branch 'feature' into main")
    {
        var before = await f.ReadAsync();
        return await new GitWriteRouter(f.Runner).ExecuteAsync(new MergeRequest(before, "feature", feature, noFastForward, message), f.Token);
    }

    // Git refuses to merge histories without a common commit, and the merge leaves the repository as it was.
    [Fact]
    public async Task WhenHistoriesAreUnrelated_ShouldRejectAndChangeNothing()
    {
        var (f, _) = await Branches(); using var _ = f;
        await f.GitAsync("switch", "--orphan", "other");
        await f.WriteAsync("other.txt", "other\n");
        var other = await f.CommitAsync("Other root", "other.txt");
        await f.GitAsync("switch", "main");
        var head = await f.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new MergeRequest(await f.ReadAsync(), "other", other, false, "Merge branch 'other' into main"), f.Token);

        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.Equal(head, await f.GitAsync("rev-parse", "HEAD"));
        var after = await f.ReadAsync();
        Assert.Equal(RepositoryOperation.None, after.Operation); Assert.False(after.HasChanges);
    }

    // The dialog's choice is on Git's command line, which wins over merge.ff in the configuration.
    [Theory]
    [InlineData("false", false)]
    [InlineData("only", true)]
    public async Task WhenMergeFfIsSetInConfig_ShouldFollowTheDialogsChoice(string configured, bool noFastForward)
    {
        var (f, feature) = await Branches(); using var _ = f;
        await f.GitAsync("config", "merge.ff", configured);

        var result = await Merge(f, feature, noFastForward);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        var parents = (await f.GitAsync("show", "-s", "--format=%P", "HEAD")).Split(' ');
        Assert.Equal(noFastForward ? 2 : 1, parents.Length);
        if (!noFastForward) Assert.Equal(feature, await f.GitAsync("rev-parse", "HEAD"));
    }

    // diff3 and zdiff3 add the base between the sides; the block is still one, and a side taken leaves only that side.
    [Theory]
    [InlineData("diff3")]
    [InlineData("zdiff3")]
    public async Task WhenConflictStyleIsDiff3OrZdiff3_ShouldCountBlocksAndTakeASide(string style)
    {
        var (f, feature) = await Branches(conflict: true); using var _ = f;
        await f.GitAsync("config", "merge.conflictStyle", style);
        Assert.Equal(GitWriteOutcome.NeedsResolution, (await Merge(f, feature)).Outcome);
        var before = await f.ReadAsync();
        var text = await new CliFileDiffReader(f.Runner).ReadConflictAsync(f.Root, before.Conflicts.Single(), f.Token);
        Assert.Equal(1, before.Resolutions.Open.Single().Blocks);

        var taken = await new GitWriteRouter(f.Runner).ExecuteAsync(new ConflictBlockRequest(before, Snapshots.Path("shared.txt"), 0, Trag.Domain.Merge.ConflictChoice.Theirs, text.Hash), f.Token);

        Assert.True(taken.Outcome == GitWriteOutcome.Succeeded, taken.Message + taken.Details);
        Assert.Equal("feature side\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "shared.txt"), f.Token));
    }

    // The writer reads the repository again with the reader it is given, so the first merge asks Git nothing that reader
    // already knows (its version): it costs what the next merge costs.
    [Fact]
    public async Task WhenTheWriterIsGivenTheShellsReader_ShouldMergeTheFirstTimeAtTheCostOfTheNext()
    {
        var (f, feature) = await Branches(); using var _ = f;
        await f.GitAsync("switch", "-c", "second", feature); await f.WriteAsync("second.txt", "two\n");
        var second = await f.CommitAsync("Second"); await f.GitAsync("switch", "main");
        var calls = 0;
        var runner = new Trag.Infrastructure.Git.Processes.GitProcessRunner(exited: _ => Interlocked.Increment(ref calls));
        var reader = new CliRepositoryReader(runner);
        var writer = new GitWriteRouter(runner, reader: reader);
        var before = await reader.ReadAsync(f.Root, f.Token); Interlocked.Exchange(ref calls, 0);
        Assert.Equal(GitWriteOutcome.Succeeded, (await writer.ExecuteAsync(new MergeRequest(before, "feature", feature, false, "Merge branch 'feature' into main"), f.Token)).Outcome);
        var first = Interlocked.Exchange(ref calls, 0);
        before = await reader.ReadAsync(f.Root, f.Token); Interlocked.Exchange(ref calls, 0);
        Assert.Equal(GitWriteOutcome.Succeeded, (await writer.ExecuteAsync(new MergeRequest(before, "second", second, false, "Merge branch 'second' into main"), f.Token)).Outcome);
        Assert.Equal(first, calls);
    }

    [Fact]
    public async Task WhenTheBranchIsAhead_ShouldFastForwardWithoutAMergeCommit()
    {
        var (f, feature) = await Branches();
        using var _ = f;
        var result = await Merge(f, feature);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("Merge completed as a fast-forward. No merge commit was created.", result.Message);
        var after = await f.ReadAsync();
        Assert.Equal(feature, after.Head); Assert.Equal("main", after.Branch); Assert.Empty(after.Files);
        Assert.Equal(RepositoryOperation.None, after.Operation);
    }

    [Fact]
    public async Task WhenNoFastForwardIsAsked_ShouldCreateAMergeCommitWithTheUsersMessage()
    {
        var (f, feature) = await Branches();
        using var _ = f;
        var main = await f.GitAsync("rev-parse", "HEAD");
        var result = await Merge(f, feature, noFastForward: true, message: "Bring the feature in\n\nReviewed by the team.");
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(main + " " + feature, await f.GitAsync("show", "-s", "--format=%P", "HEAD"));
        Assert.Equal("Bring the feature in\n\nReviewed by the team.", await f.GitAsync("show", "-s", "--format=%B", "HEAD"));
        Assert.Equal(await f.GitAsync("rev-parse", "HEAD"), result.CommitId);
        Assert.Empty((await f.ReadAsync()).Files);
    }

    // Trag hands Git the user's merge message in UTF-8; a commit encoding the user set for other tools must not label those
    // bytes as another encoding.
    [Fact]
    public async Task WhenCommitEncodingIsCp1250AndTheMergeCommitGetsTheUsersMessage_ShouldWriteBytesThatMatchTheEncodingTheCommitNames()
    {
        var (f, feature) = await Branches(ownCommit: true);
        using var _ = f;
        await f.GitAsync("config", "i18n.commitEncoding", "cp1250");

        var result = await Merge(f, feature, message: "Spoji čvor đak");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("Spoji čvor đak", await f.GitAsync("log", "-1", "--encoding=UTF-8", "--format=%B"));
    }

    [Fact]
    public async Task WhenTheBranchesHaveDiverged_ShouldCreateAMergeCommitWithBothParentsThatTheReflogCreditsToTrag()
    {
        var (f, feature) = await Branches(ownCommit: true);
        using var _ = f;
        var main = await f.GitAsync("rev-parse", "HEAD");
        var result = await Merge(f, feature);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("Merge completed with a merge commit. Both parents and the staged tree were verified.", result.Message);
        Assert.Equal(main + " " + feature, await f.GitAsync("show", "-s", "--format=%P", "HEAD"));
        Assert.StartsWith("trag-merge-", await f.GitAsync("reflog", "-1", "--format=%gs", "HEAD"));
        Assert.Equal("two\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "feature.txt"), f.Token));
        Assert.Equal("main side\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "main.txt"), f.Token));
    }

    [Fact]
    public async Task WhenTheBranchesConflict_ShouldLeaveAMergeInProgressWithTheUsersMessage()
    {
        var (f, feature) = await Branches(conflict: true);
        using var _ = f;
        var result = await Merge(f, feature, message: "Merge the feature, conflicts and all");
        Assert.True(result.Outcome == GitWriteOutcome.NeedsResolution, result.Message + result.Details);
        var after = await f.ReadAsync();
        Assert.True(after.HasSupportedMerge);
        Assert.Equal(feature, after.Merge!.IncomingHead);
        Assert.Equal("shared.txt", Assert.Single(after.Conflicts).Path.Display);
        Assert.StartsWith("Merge the feature, conflicts and all", after.Merge.Message);
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "index.lock")));
    }

    // A line of the dialog's message that starts with '#', an issue number, is the user's: the merge view offers the
    // message back without Git's list of the conflicts, written with core.commentChar, and Finish commits it.
    [Theory]
    [InlineData("#")]
    [InlineData(";")]
    public async Task WhenTheMessageHasALineStartingWithAHashAndTheMergeStops_ShouldFinishWithItAndWithoutGitsConflictList(string comment)
    {
        var (f, feature) = await Branches(conflict: true);
        using var _ = f;
        await f.GitAsync("config", "core.commentChar", comment);
        Assert.Equal(GitWriteOutcome.NeedsResolution, (await Merge(f, feature, message: "#123 Merge the feature")).Outcome);
        await f.WriteAsync("shared.txt", "resolved\n"); await f.GitAsync("add", "shared.txt");
        var stopped = await f.ReadAsync();

        var finished = await new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(stopped, MergeMessages.Clean(stopped.Merge!.Message), FinishesMerge: true), f.Token);

        Assert.True(finished.Outcome == GitWriteOutcome.Succeeded, finished.Message + finished.Details);
        Assert.Equal("#123 Merge the feature", await f.GitAsync("log", "-1", "--format=%B"));
    }

    [Fact]
    public async Task WhenTheWorkingTreeHasChanges_ShouldRefuseTheMergeAndChangeNothing()
    {
        var (f, feature) = await Branches();
        using var _ = f;
        var main = await f.GitAsync("rev-parse", "HEAD");
        await f.WriteAsync("shared.txt", "local edit\n");
        var result = await Merge(f, feature);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.Equal("Merge needs a clean working tree and index. Commit or stash your changes first.", result.Message);
        Assert.Equal(main, await f.GitAsync("rev-parse", "HEAD"));
        Assert.Equal("local edit\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "shared.txt"), f.Token));
    }

    // Git refuses a fast-forward over an ignored file, but an ort merge commit overwrites it in spite of
    // --no-overwrite-ignore; either way the user's file stays and nothing is merged.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAnIncomingFileWouldOverwriteAnIgnoredLocalFile_ShouldRefuseAndKeepTheIgnoredFile(bool ownCommit)
    {
        var (f, feature) = await IgnoredEnv(ownCommit); using var _ = f;
        var main = await f.GitAsync("rev-parse", "HEAD");
        await f.WriteAsync(".env", "SECRET=mine\n");

        var result = await Merge(f, feature);

        Assert.Equal((GitWriteOutcome.Rejected, MergeOutcomeText.IgnoredInTheWay), (result.Outcome, result.Message));
        Assert.Equal(".env", result.Details);
        Assert.Equal("SECRET=mine\n", await File.ReadAllTextAsync(Path.Combine(f.Root, ".env"), f.Token));
        Assert.Equal(main, await f.GitAsync("rev-parse", "HEAD"));
        Assert.Equal(RepositoryOperation.None, (await f.ReadAsync()).Operation);
    }

    // An ort merge commit also replaces an ignored file that stands where the incoming files need a folder, and an
    // ignored folder that stands where an incoming file goes; each is named once.
    [Theory]
    [InlineData("build", "build/out.txt", "build/more.txt")]
    [InlineData("logs", "logs")]
    public async Task WhenAnIncomingFileWouldReplaceAnIgnoredFileOrFolderInItsWay_ShouldRefuseAndKeepIt(string ignored, params string[] incoming)
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync(".gitignore", ignored + "\n"); await f.CommitAsync("Ignore " + ignored);
        await f.GitAsync("switch", "-c", "feature");
        foreach (var path in incoming) { await f.WriteAsync(path, "incoming\n"); await f.GitAsync("add", "--force", path); }
        var feature = await f.CommitAsync("Bring " + ignored);
        await f.GitAsync("switch", "main"); await f.WriteAsync("main.txt", "main side\n"); await f.CommitAsync("Main own");
        var mine = incoming[0] == ignored ? Path.Combine(ignored, "mine.log") : ignored;
        await f.WriteAsync(mine, "mine\n");

        var result = await Merge(f, feature);

        Assert.Equal((GitWriteOutcome.Rejected, MergeOutcomeText.IgnoredInTheWay, ignored), (result.Outcome, result.Message, result.Details));
        Assert.Equal("mine\n", await File.ReadAllTextAsync(Path.Combine(f.Root, mine), f.Token));
    }

    // A file the user ignores but does not have is just a file the merge brings in.
    [Fact]
    public async Task WhenAnIncomingFileIsIgnoredHereButNotThere_ShouldMergeIt()
    {
        var (f, feature) = await IgnoredEnv(ownCommit: true); using var _ = f;

        var result = await Merge(f, feature);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("SECRET=feature\n", await File.ReadAllTextAsync(Path.Combine(f.Root, ".env"), f.Token));
    }

    // On a file system that ignores case, the renamed file is already here under its old name; it is not in the way.
    [Fact]
    public async Task WhenTheIncomingCommitRenamesAFileOnlyByCase_ShouldMergeIt()
    {
        var (f, _) = await IgnoredEnv(ownCommit: false); using var _ = f;
        await f.WriteAsync("Readme.md", "readme\n"); await f.CommitAsync("Readme");
        await f.GitAsync("switch", "-c", "renamed"); await f.GitAsync("mv", "Readme.md", "README.md");
        var renamed = await f.CommitAsync("Rename the readme");
        await f.GitAsync("switch", "main"); await f.WriteAsync("main.txt", "main side\n"); await f.CommitAsync("Main own");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new MergeRequest(await f.ReadAsync(), "renamed", renamed, false, "Merge branch 'renamed' into main"), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("README.md", await f.GitAsync("ls-files", "README.md"));
    }

    // main stopped tracking .env and ignores the one it keeps; feature, branched before, still tracks it. Left alone
    // there, .env is not something the merge brings in, so Git leaves the local one as it is.
    [Fact]
    public async Task WhenTheCurrentBranchStoppedTrackingAnIgnoredFileTheIncomingSideLeftAlone_ShouldMergeAndKeepIt()
    {
        var (f, feature) = await StoppedTrackingEnv(incomingChangesIt: false); using var _ = f;

        var result = await Merge(f, feature);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Outcome + " " + result.Message + " | " + result.Details);
        Assert.Equal("SECRET=mine\n", await File.ReadAllTextAsync(Path.Combine(f.Root, ".env"), f.Token));
    }

    // Changed there, .env is a modify/delete conflict whose checkout writes feature's .env over the local one.
    [Fact]
    public async Task WhenTheCurrentBranchStoppedTrackingAnIgnoredFileTheIncomingSideChanged_ShouldRefuseAndKeepIt()
    {
        var (f, feature) = await StoppedTrackingEnv(incomingChangesIt: true); using var _ = f;

        var result = await Merge(f, feature);

        Assert.Equal((GitWriteOutcome.Rejected, MergeOutcomeText.IgnoredInTheWay, ".env"), (result.Outcome, result.Message, result.Details));
        Assert.Equal("SECRET=mine\n", await File.ReadAllTextAsync(Path.Combine(f.Root, ".env"), f.Token));
    }

    private static async Task<(GitFixture Fixture, string Feature)> StoppedTrackingEnv(bool incomingChangesIt)
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.WriteAsync(".env", "SECRET=shared\n"); await f.CommitAsync("Track .env");
            await f.GitAsync("switch", "-c", "feature");
            await f.WriteAsync(incomingChangesIt ? ".env" : "feature.txt", "SECRET=feature\n");
            var feature = await f.CommitAsync("Feature");
            await f.GitAsync("switch", "main");
            await f.GitAsync("rm", "-q", "--cached", ".env");
            await f.WriteAsync(".gitignore", ".env\n"); await f.CommitAsync("Stop tracking .env");
            await f.WriteAsync(".env", "SECRET=mine\n");
            return (f, feature);
        }
        catch { f.Dispose(); throw; }
    }

    // main ignores .env; feature commits one anyway, and main gets a commit of its own when asked, so the merge needs a
    // merge commit instead of a fast-forward.
    private static async Task<(GitFixture Fixture, string Feature)> IgnoredEnv(bool ownCommit)
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.WriteAsync(".gitignore", ".env\n"); await f.CommitAsync("Ignore .env");
            await f.GitAsync("switch", "-c", "feature");
            await f.WriteAsync(".env", "SECRET=feature\n"); await f.GitAsync("add", "--force", ".env");
            var feature = await f.CommitAsync("Track .env");
            await f.GitAsync("switch", "main");
            if (ownCommit) { await f.WriteAsync("main.txt", "main side\n"); await f.CommitAsync("Main own"); }
            return (f, feature);
        }
        catch { f.Dispose(); throw; }
    }

    // The dialog's conflicts come from merge-tree and the merge is git merge; both apply the merge drivers of
    // .gitattributes and merge.renormalize, so the merge leaves exactly the conflicts the preview named.
    [Theory]
    [InlineData("merge=union", false)]
    [InlineData("merge=ours", false)]
    [InlineData("merge=binary", true)]
    [InlineData("binary", true)]
    [InlineData("renormalize", false)]
    public async Task WhenGitattributesDefineMergeDrivers_ShouldMergeAsThePreviewSaid(string attribute, bool conflicts)
    {
        using var f = await GitFixture.CreateAsync("main");
        var renormalize = attribute == "renormalize";
        await f.GitAsync("config", "merge.ours.driver", "true");
        await f.GitAsync("config", "merge.renormalize", "true");
        if (!renormalize) await f.WriteAsync(".gitattributes", "a.txt " + attribute + "\n");
        await f.WriteAsync("a.txt", renormalize ? "one\r\ntwo\r\nthree\r\n" : "one\ntwo\nthree\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("a.txt", renormalize ? "one\r\ntwo\r\nthree\r\nfour\r\n" : "one\nfeature\nthree\n");
        var feature = await f.CommitAsync("Feature");
        await f.GitAsync("switch", "main");
        if (renormalize)
        {
            await f.WriteAsync(".gitattributes", "a.txt text eol=lf\n"); await f.GitAsync("add", ".gitattributes"); await f.GitAsync("add", "--renormalize", ".");
            await f.CommitAsync("Normalize");
        }
        else { await f.WriteAsync("a.txt", "one\nmain\nthree\n"); await f.CommitAsync("Main"); }
        var preview = await new CliBranchReader(f.Runner).PreviewMergeAsync(f.Root, "refs/heads/main", "refs/heads/feature", f.Token);

        var result = await Merge(f, feature);

        Assert.Equal(conflicts, preview.HasConflicts);
        Assert.Equal(conflicts ? GitWriteOutcome.NeedsResolution : GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal(preview.Conflicts!.Select(path => path.Display), (await f.ReadAsync()).Conflicts.Select(file => file.Path.Display));
    }

    // Cancel stops Git in its post-merge hook, after the merge itself; the verification finds what Git did and says the
    // merge completed although it was interrupted.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenCanceledDuringTheMerge_ShouldReportFastForwardOrMergeCommitAfterInterruption(bool ownCommit)
    {
        var (f, feature) = await Branches(ownCommit); using var _ = f;
        var main = await f.GitAsync("rev-parse", "HEAD");
        await Hook(f, "post-merge", "sleep 30 & echo ready > .git/trag-hook-ready\nwait\n");
        using var cancellation = new CancellationTokenSource();
        var pending = new GitWriteRouter(f.Runner).ExecuteAsync(new MergeRequest(await f.ReadAsync(), "feature", feature, false, "Merge branch 'feature' into main"), cancellation.Token);
        while (!File.Exists(Path.Combine(f.Root, ".git", "trag-hook-ready")))
        {
            if (pending.IsCompleted) throw new InvalidOperationException((await pending).Message);
            await Task.Delay(30, f.Token);
        }

        cancellation.Cancel();
        var result = await pending;

        Assert.Equal((GitWriteOutcome.Succeeded, ownCommit ? MergeOutcomeText.MergeCommitAfterInterruption : MergeOutcomeText.FastForwardAfterInterruption),
            (result.Outcome, result.Message));
        Assert.Equal(ownCommit ? main + " " + feature : feature, await f.GitAsync(ownCommit ? ["show", "-s", "--format=%P", "HEAD"] : ["rev-parse", "HEAD"]));
        var after = await f.ReadAsync();
        Assert.Equal(RepositoryOperation.None, after.Operation); Assert.Empty(after.Files);
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "index.lock")));
    }

    // A merge Git made that Trag cannot read back keeps its index for review instead of publishing it unverified.
    [Fact]
    public async Task WhenTheMergeCannotBeVerified_ShouldKeepItsIndexForReview()
    {
        var (f, feature) = await Branches(ownCommit: true); using var _ = f;
        var before = await f.ReadAsync();
        var writer = new GitWriteRouter(f.Runner, reader: new UnreadableAfterHeadMoves(new CliRepositoryReader(f.Runner), before.Head!));

        var result = await writer.ExecuteAsync(new MergeRequest(before, "feature", feature, false, "Merge branch 'feature' into main"), f.Token);

        Assert.Equal((GitWriteOutcome.Uncertain, MergeOutcomeText.Branch.Unsynchronized), (result.Outcome, result.Message));
        Assert.Contains(WriteText.RecoveryIndex(""), result.Details);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(f.Root, ".git"), "trag-write-*.index"));
    }

    private sealed class UnreadableAfterHeadMoves(IRepositoryReader reader, string head) : IRepositoryReader
    {
        public async Task<RepositorySnapshot> ReadAsync(string path, CancellationToken ct)
        {
            var snapshot = await reader.ReadAsync(path, ct);
            return snapshot.Head == head ? snapshot : throw new IOException("The repository could not be read.");
        }
    }

    // A merge brings the first Git LFS files to a branch that had none; checking out their content may take longer than
    // the limit a hung merge gets, which a pre-merge-commit hook that outlasts it stands in for here.
    [Fact]
    public async Task WhenTheMergeBringsTheFirstLfsFilesAndRunsLongerThanTwoMinutes_ShouldNotKillItHalfway()
    {
        using var f = await GitFixture.CreateAsync("main", scenarioTimeout: TimeSpan.FromMinutes(6));
        await LfsConfig.SetAsync(f, f.Root, installed: true);
        await f.WriteAsync("a.txt", "base\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n"); await f.WriteAsync("asset.bin", "content\n");
        var feature = await f.CommitAsync("Asset");
        await f.GitAsync("switch", "main"); await f.WriteAsync("main.txt", "main\n"); await f.CommitAsync("Main own");
        await Hook(f, "pre-merge-commit", "sleep 130\n");

        var result = await Merge(f, feature);

        Assert.Equal((GitWriteOutcome.Succeeded, MergeOutcomeText.Branch.MergeCommit), (result.Outcome, result.Message));
        Assert.Equal("content\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "asset.bin"), f.Token));
    }

    [Fact]
    public async Task WhenIndexLockIsHeldAtStart_ShouldRefuseWithoutTouchingIt()
    {
        var (f, feature) = await Branches(ownCommit: true); using var _ = f;
        var main = await f.GitAsync("rev-parse", "HEAD");
        var held = Path.Combine(f.Root, ".git", "index.lock");
        await File.WriteAllTextAsync(held, "another program\n", f.Token);

        var result = await Merge(f, feature);

        Assert.Equal((GitWriteOutcome.Rejected, WriteText.IndexLockHeld), (result.Outcome, result.Message));
        Assert.Equal("another program\n", await File.ReadAllTextAsync(held, f.Token));
        Assert.Equal(main, await f.GitAsync("rev-parse", "HEAD"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(f.Root, ".git"), "trag-write-*.index"));
    }

    // merge.log adds Git's list of the merged commits to a message given with -m; the dialog showed the message alone, so
    // that is what the merge commit, or the merge left for its conflicts, carries.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenMergeLogIsSet_ShouldUseTheMessageTheDialogShowed(bool conflict)
    {
        var (f, feature) = await Branches(ownCommit: true, conflict: conflict); using var _ = f;
        await f.GitAsync("config", "merge.log", "true");

        var result = await Merge(f, feature, message: "Bring the feature in");

        Assert.Equal(conflict ? GitWriteOutcome.NeedsResolution : GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("Bring the feature in", conflict ? MergeMessages.Clean((await f.ReadAsync()).Merge!.Message) : await f.GitAsync("log", "-1", "--format=%B"));
    }

    // merge.autoStash would put away a change made after Trag found the tree clean, merge, and bring the change back,
    // conflicting with the merge; the merge never runs over the user's edit instead.
    [Fact]
    public async Task WhenMergeAutoStashIsSetAndAFileChangesAfterTheCheck_ShouldNotStashIt()
    {
        var (f, feature) = await Branches(ownCommit: true, conflict: true); using var _ = f;
        await f.GitAsync("config", "merge.autoStash", "true");
        var main = await f.GitAsync("rev-parse", "HEAD");
        var before = await f.ReadAsync();
        var writer = new GitWriteRouter(f.Runner, reader: new EditsUnderTheLock(new CliRepositoryReader(f.Runner), f.Root));

        var result = await writer.ExecuteAsync(new MergeRequest(before, "feature", feature, false, "Merge branch 'feature' into main"), f.Token);

        Assert.NotEqual(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal(main, await f.GitAsync("rev-parse", "HEAD"));
        Assert.Equal("edited\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "shared.txt"), f.Token));
        Assert.Equal("", await f.GitAsync("stash", "list"));
    }

    // The user edits shared.txt just after the merge read the repository under its lock.
    private sealed class EditsUnderTheLock(IRepositoryReader reader, string root) : IRepositoryReader
    {
        private bool edited;

        public async Task<RepositorySnapshot> ReadAsync(string path, CancellationToken ct)
        {
            var snapshot = await reader.ReadAsync(path, ct);
            if (!edited && File.Exists(Path.Combine(root, ".git", "index.lock")))
            {
                edited = true;
                await File.WriteAllTextAsync(Path.Combine(root, "shared.txt"), "edited\n", ct);
            }
            return snapshot;
        }
    }

    // A user who requires signed commits keeps that rule: Git refuses an unsigned branch and nothing is merged.
    [Fact]
    public async Task WhenMergeVerifySignaturesIsSetAndTheBranchIsNotSigned_ShouldRejectWithGitsReason()
    {
        var (f, feature) = await Branches(ownCommit: true); using var _ = f;
        await f.GitAsync("config", "merge.verifySignatures", "true");
        var main = await f.GitAsync("rev-parse", "HEAD");

        var result = await Merge(f, feature);

        Assert.Equal((GitWriteOutcome.Rejected, MergeOutcomeText.Rejected), (result.Outcome, result.Message));
        Assert.Contains("does not have a GPG signature", result.Details);
        Assert.Equal(main, await f.GitAsync("rev-parse", "HEAD"));
    }

    // The branch's own merge options are cleared: neither --squash nor a strategy option changes the merge the dialog
    // showed, here one that stops at its conflict.
    [Fact]
    public async Task WhenTheBranchHasMergeOptions_ShouldMergeAsTheDialogSaid()
    {
        var (f, feature) = await Branches(conflict: true); using var _ = f;
        await f.GitAsync("config", "branch.main.mergeOptions", "--squash -Xtheirs");

        var result = await Merge(f, feature);

        Assert.True(result.Outcome == GitWriteOutcome.NeedsResolution, result.Message + result.Details);
        Assert.Equal(feature, (await f.ReadAsync()).Merge!.IncomingHead);
    }

    private static async Task Hook(GitFixture f, string name, string body)
    {
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(f.Container, "empty-hooks")).FullName, name);
        await File.WriteAllTextAsync(path, "#!/bin/sh\n" + body, new System.Text.UTF8Encoding(false), f.Token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public async Task WhenTheCurrentBranchAlreadyContainsTheChosenOne_ShouldMergeNothing()
    {
        var (f, feature) = await Branches();
        using var _ = f;
        await f.GitAsync("switch", "feature");
        var main = await f.GitAsync("rev-parse", "main");
        var before = await f.ReadAsync();
        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new MergeRequest(before, "main", main, false, "Merge branch 'main' into feature"), f.Token);
        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("feature already contains main. Nothing was merged.", result.Message);
        Assert.Equal(feature, await f.GitAsync("rev-parse", "HEAD"));
    }
}
