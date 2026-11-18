using System.Text;
using Trag.IntegrationTests.Git.Sync;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

public sealed class StashTests
{
    [Fact]
    public async Task WhenStashesArePushedThenAppliedAndDroppedById_ShouldChangeOnlyTheChosenEntryAndItsFiles()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        var stashes = new CliStashReader(new());
        var shared = Path.Combine(f.One, "shared.txt"); var extra = Path.Combine(f.One, "extra.txt");
        var original = await File.ReadAllTextAsync(shared, f.Token);
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Null(snapshot.StashHead);
        Assert.Equal(GitWriteOutcome.Rejected, (await writer.ExecuteAsync(new StashRequest(snapshot, new("nothing"), StashAction.Push), f.Token)).Outcome);

        // Tracked changes go into the stash; an untracked file stays behind unless asked for.
        await File.WriteAllTextAsync(shared, original + "stashed line\n", f.Token);
        await File.WriteAllTextAsync(extra, "new file\n", f.Token);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Equal(2, snapshot.Files.Count);
        var pushed = await writer.ExecuteAsync(new StashRequest(snapshot, new(""), StashAction.Push), f.Token);
        Assert.True(pushed.Outcome == GitWriteOutcome.Succeeded, pushed.Message + pushed.Details);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.NotNull(snapshot.StashHead); Assert.Single(snapshot.Files); Assert.Equal(original, await File.ReadAllTextAsync(shared, f.Token));
        var list = await stashes.ReadStashesAsync(f.One, f.Token);
        var first = Assert.Single(list);
        Assert.Equal(snapshot.StashHead, first.Id); Assert.Equal("trunk", first.Branch); Assert.StartsWith("WIP on trunk: ", first.Message);

        // A named stash with the untracked file becomes stash@{0} and the file leaves the working tree; the older one moves to stash@{1}.
        var named = await writer.ExecuteAsync(new StashRequest(snapshot, new("keep the extra file", IncludeUntracked: true), StashAction.Push), f.Token);
        Assert.True(named.Outcome == GitWriteOutcome.Succeeded, named.Message + named.Details);
        Assert.False(File.Exists(extra), named.Message + named.Details);
        list = await stashes.ReadStashesAsync(f.One, f.Token);
        Assert.Equal(2, list.Count); Assert.Equal("keep the extra file", list[0].Title); Assert.Equal(first.Id, list[1].Id); Assert.Equal(1, list[1].Index);

        // Apply by id, not by index: the older entry comes back and stays listed; an unknown id is refused.
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var applied = await writer.ExecuteAsync(new StashRequest(snapshot, new(Id: first.Id), StashAction.Apply), f.Token);
        Assert.True(applied.Outcome == GitWriteOutcome.Succeeded, applied.Message + applied.Details);
        Assert.Equal(original + "stashed line\n", await File.ReadAllTextAsync(shared, f.Token));
        Assert.Equal(2, (await stashes.ReadStashesAsync(f.One, f.Token)).Count);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Equal(GitWriteOutcome.Rejected, (await writer.ExecuteAsync(new StashRequest(snapshot, new(Id: new string('0', 40)), StashAction.Apply), f.Token)).Outcome);

        // Drop removes exactly the chosen entry; the untracked stash restores its file, and applying over an existing file is refused.
        var dropped = await writer.ExecuteAsync(new StashRequest(snapshot, new(Id: first.Id), StashAction.Drop), f.Token);
        Assert.True(dropped.Outcome == GitWriteOutcome.Succeeded, dropped.Message + dropped.Details);
        list = await stashes.ReadStashesAsync(f.One, f.Token);
        var remaining = Assert.Single(list); Assert.Equal("keep the extra file", remaining.Title); Assert.Equal(0, remaining.Index);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var restored = await writer.ExecuteAsync(new StashRequest(snapshot, new(Id: remaining.Id), StashAction.Apply), f.Token);
        Assert.True(restored.Outcome == GitWriteOutcome.Succeeded, restored.Message + restored.Details); Assert.Equal("new file\n", await File.ReadAllTextAsync(extra, f.Token));
        await File.WriteAllTextAsync(extra, "different content\n", f.Token);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var refused = await writer.ExecuteAsync(new StashRequest(snapshot, new(Id: remaining.Id), StashAction.Apply), f.Token);
        Assert.Equal(GitWriteOutcome.Rejected, refused.Outcome); Assert.Contains("already exist", refused.Message); Assert.Equal("different content\n", await File.ReadAllTextAsync(extra, f.Token));
        Assert.Single(await stashes.ReadStashesAsync(f.One, f.Token));
    }

    // A stash keeps nothing of a submodule's checkout, so a tree where only a submodule moved is refused with that reason,
    // and the submodule stays where it is.
    [Fact]
    public async Task WhenOnlyASubmoduleMoved_ShouldRefuseTheStashAndSayASubmoduleIsNotStashed()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var sub = Path.Combine(f.Root, "sub");
        await f.GitAtAsync(sub, "commit", "--allow-empty", "-m", "Sub two");
        var moved = await f.GitAtAsync(sub, "rev-parse", "HEAD");

        var refused = await new GitWriteRouter(f.Runner).ExecuteAsync(new StashRequest(await f.ReadAsync(), new("only the submodule"), StashAction.Push), f.Token);

        Assert.Equal(GitWriteOutcome.Rejected, refused.Outcome);
        Assert.Equal(Trag.Domain.Presentation.Text.StashOutcomeText.OnlySubmodules, refused.Message);
        Assert.Equal(moved, await f.GitAtAsync(sub, "rev-parse", "HEAD"));
        Assert.Equal("", await f.GitAsync("stash", "list"));
    }

    // On a detached HEAD Git writes "(no branch)" where the branch goes, and the entry is listed like any other.
    [Fact]
    public async Task WhenStashingOnADetachedHead_ShouldStashAndListWithoutABranch()
    {
        using var f = await SyncFixture.Create();
        var shared = Path.Combine(f.One, "shared.txt");
        var original = await File.ReadAllTextAsync(shared, f.Token);
        await f.Git(f.One, "switch", "--detach");
        await File.WriteAllTextAsync(shared, original + "detached\n", f.Token);

        var pushed = await new GitWriteRouter(new(), f.Sync).ExecuteAsync(
            new StashRequest(await f.Reader.ReadAsync(f.One, f.Token), new("on no branch"), StashAction.Push), f.Token);

        Assert.True(pushed.Outcome == GitWriteOutcome.Succeeded, pushed.Message + pushed.Details);
        var entry = Assert.Single(await new CliStashReader(new()).ReadStashesAsync(f.One, f.Token));
        Assert.Equal(("(no branch)", "on no branch"), (entry.Branch, entry.Title));
        Assert.Equal(original, await File.ReadAllTextAsync(shared, f.Token));
    }

    // An apply that would overwrite a local change of the same file is refused, as Git refuses it: the change and the
    // entry both stay.
    [Fact]
    public async Task WhenAStashIsAppliedOverADirtyTree_ShouldRefuseWhenGitWould()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        var shared = Path.Combine(f.One, "shared.txt");
        var original = await File.ReadAllTextAsync(shared, f.Token);
        await File.WriteAllTextAsync(shared, original + "stashed\n", f.Token);
        await writer.ExecuteAsync(new StashRequest(await f.Reader.ReadAsync(f.One, f.Token), new("first"), StashAction.Push), f.Token);
        var entry = Assert.Single(await new CliStashReader(new()).ReadStashesAsync(f.One, f.Token));
        await File.WriteAllTextAsync(shared, original + "local\n", f.Token);

        var refused = await writer.ExecuteAsync(new StashRequest(await f.Reader.ReadAsync(f.One, f.Token), new(Id: entry.Id), StashAction.Apply), f.Token);

        Assert.Equal(GitWriteOutcome.Rejected, refused.Outcome);
        Assert.Equal(original + "local\n", await File.ReadAllTextAsync(shared, f.Token));
        Assert.Single(await new CliStashReader(new()).ReadStashesAsync(f.One, f.Token));
    }

    // Git applies a stash whose change the branch has committed since and leaves the status as it was: that is an apply,
    // not an outcome to review.
    [Fact]
    public async Task WhenAStashWhoseChangeTheBranchAlreadyHasIsApplied_ShouldReportItApplied()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        var shared = Path.Combine(f.One, "shared.txt");
        var original = await File.ReadAllTextAsync(shared, f.Token);
        await File.WriteAllTextAsync(shared, original + "stashed\n", f.Token);
        await writer.ExecuteAsync(new StashRequest(await f.Reader.ReadAsync(f.One, f.Token), new("committed since"), StashAction.Push), f.Token);
        var entry = Assert.Single(await new CliStashReader(new()).ReadStashesAsync(f.One, f.Token));
        await File.WriteAllTextAsync(shared, original + "stashed\n", f.Token);
        await f.Git(f.One, "commit", "--all", "-m", "The stashed change");

        var applied = await writer.ExecuteAsync(new StashRequest(await f.Reader.ReadAsync(f.One, f.Token), new(Id: entry.Id), StashAction.Apply), f.Token);

        Assert.Equal((GitWriteOutcome.Succeeded, StashOutcomeText.Applied(entry.Reference)), (applied.Outcome, applied.Message));
    }

    // An untracked file of the stash that is ignored since comes back without changing the status.
    [Fact]
    public async Task WhenAStashWhoseUntrackedFileIsIgnoredSinceIsApplied_ShouldBringTheFileBackAndReportItApplied()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        var extra = Path.Combine(f.One, "extra.txt");
        await File.WriteAllTextAsync(extra, "new file\n", f.Token);
        await writer.ExecuteAsync(new StashRequest(await f.Reader.ReadAsync(f.One, f.Token), new("ignored since", IncludeUntracked: true), StashAction.Push), f.Token);
        var entry = Assert.Single(await new CliStashReader(new()).ReadStashesAsync(f.One, f.Token));
        await File.WriteAllTextAsync(Path.Combine(f.One, ".gitignore"), "extra.txt\n", f.Token);
        await f.Git(f.One, "add", ".gitignore"); await f.Git(f.One, "commit", "-m", "Ignore the extra file");

        var applied = await writer.ExecuteAsync(new StashRequest(await f.Reader.ReadAsync(f.One, f.Token), new(Id: entry.Id), StashAction.Apply), f.Token);

        Assert.Equal((GitWriteOutcome.Succeeded, StashOutcomeText.Applied(entry.Reference)), (applied.Outcome, applied.Message));
        Assert.Equal("new file\n", await File.ReadAllTextAsync(extra, f.Token));
    }

    // Apply runs without --index, so a file with a staged and an unstaged change comes back with both, unstaged.
    [Fact]
    public async Task WhenAStashHoldsStagedAndUnstagedChangesOfOneFile_ShouldApplyThemAsUnstaged()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        var shared = Path.Combine(f.One, "shared.txt");
        var original = await File.ReadAllTextAsync(shared, f.Token);
        await File.WriteAllTextAsync(shared, original + "staged\n", f.Token); await f.Git(f.One, "add", "shared.txt");
        await File.WriteAllTextAsync(shared, original + "staged\nunstaged\n", f.Token);
        await writer.ExecuteAsync(new StashRequest(await f.Reader.ReadAsync(f.One, f.Token), new("both"), StashAction.Push), f.Token);
        var entry = Assert.Single(await new CliStashReader(new()).ReadStashesAsync(f.One, f.Token));

        var applied = await writer.ExecuteAsync(new StashRequest(await f.Reader.ReadAsync(f.One, f.Token), new(Id: entry.Id), StashAction.Apply), f.Token);

        Assert.True(applied.Outcome == GitWriteOutcome.Succeeded, applied.Message + applied.Details);
        Assert.Equal(original + "staged\nunstaged\n", await File.ReadAllTextAsync(shared, f.Token));
        Assert.Equal("", await f.Git(f.One, "diff", "--cached", "--name-only"));
    }

    [Fact]
    public async Task WhenOnlyUntrackedFilesChanged_ShouldRefuseToStashUntilIncludeUntrackedIsTurnedOn()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        await File.WriteAllTextAsync(Path.Combine(f.One, "extra.txt"), "new file\n", f.Token);
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Single(snapshot.Files);

        // Git saves nothing and still exits 0. Refused, not uncertain: an uncertain outcome puts the shell into review
        // and blocks every later write until a refresh, and there is nothing here that needs reviewing.
        var refused = await writer.ExecuteAsync(new StashRequest(snapshot, new("only untracked"), StashAction.Push), f.Token);
        Assert.Equal(GitWriteOutcome.Rejected, refused.Outcome);
        Assert.Equal("Nothing was stashed: every change is in a file Git does not track yet. Turn on \"Include untracked files\" and stash again.", refused.Message);
        Assert.Null((await f.Reader.ReadAsync(f.One, f.Token)).StashHead);
        Assert.True(File.Exists(Path.Combine(f.One, "extra.txt")));

        // With the checkbox on, the same working tree stashes and the file leaves.
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var pushed = await writer.ExecuteAsync(new StashRequest(snapshot, new("only untracked", IncludeUntracked: true), StashAction.Push), f.Token);
        Assert.True(pushed.Outcome == GitWriteOutcome.Succeeded, pushed.Message + pushed.Details);
        Assert.False(File.Exists(Path.Combine(f.One, "extra.txt")));
    }

    // A change shown in the list can be undone in an editor before the click. Git then saves nothing and still exits 0,
    // saying there are no local changes to save, and with untracked files included nothing at all is left to stash.
    [Fact]
    public async Task WhenTheShownChangeIsUndoneBeforeAStashWithUntrackedFiles_ShouldRefuseAndSayThereIsNothingToStash()
    {
        using var f = await GitFixture.CreateAsync();
        await f.WriteAsync("x.txt", "decoy\n");
        await f.CommitAsync("Decoy", "x.txt");
        await f.WriteAsync("x.txt", "decoy\nchanged\n");
        var shown = await f.ReadAsync();
        await f.WriteAsync("x.txt", "decoy\n");

        var refused = await new GitWriteRouter(f.Runner).ExecuteAsync(new StashRequest(shown, new("nothing left", IncludeUntracked: true), StashAction.Push), f.Token);

        Assert.Equal(GitWriteOutcome.Rejected, refused.Outcome);
        Assert.Equal("There is nothing to stash: the working tree is clean.", refused.Message);
        Assert.Equal("", await f.GitAsync("stash", "list"));
    }

    // The untracked file goes into the stash under exactly its name and comes back with its bytes when it is applied.
    [Theory]
    [MemberData(nameof(HostileNameRows.Tracked), MemberType = typeof(HostileNameRows))]
    public async Task WhenAnUntrackedFileHasAHostileName_ShouldStashItWithUntrackedFilesAndApplyItBack(string label)
    {
        var name = TestNames.Find(label).Path;
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("x.txt", "decoy\n");
        await f.CommitAsync("Decoy", "x.txt");
        await f.WriteAsync(name, "hostile\n");
        var writer = new GitWriteRouter(f.Runner);

        var pushed = await writer.ExecuteAsync(new StashRequest(await f.ReadAsync(), new("with untracked files", IncludeUntracked: true), StashAction.Push), f.Token);
        var gone = !File.Exists(Path.Combine(f.Root, name));
        var stashed = Encoding.UTF8.GetString((await f.RunAsync("ls-tree", "-r", "-z", "--name-only", "stash@{0}^3")).StandardOutput);
        var entry = Assert.Single(await new CliStashReader(f.Runner).ReadStashesAsync(f.Root, f.Token));
        var applied = await writer.ExecuteAsync(new StashRequest(await f.ReadAsync(), new(Id: entry.Id), StashAction.Apply), f.Token);

        Assert.Equal((GitWriteOutcome.Succeeded, GitWriteOutcome.Succeeded), (pushed.Outcome, applied.Outcome));
        Assert.True(gone);
        Assert.Equal(name + "\0", stashed);
        Assert.Equal("hostile\n"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(f.Root, name), f.Token));
    }

    // Windows 11 writes a reserved device name with an extension as an ordinary file, which Git for Windows cannot index:
    // a stash with untracked files skips it and then deletes it with the rest, so it would be in neither the stash nor
    // the working tree. Such a stash is refused before the stash runs, with Git's reason naming the file as the details
    // and not the list of the files Git went through before it, such as 0.txt, and nothing moves.
    [Theory]
    [MemberData(nameof(HostileNameRows.Unindexable), MemberType = typeof(HostileNameRows))]
    public async Task WhenAnUntrackedFileHasANameGitForWindowsCannotIndex_ShouldRefuseToStashWithUntrackedFilesAndKeepTheFile(string label)
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = TestNames.Find(label).Path;
        using var f = await GitFixture.CreateAsync();
        await f.WriteAsync("x.txt", "decoy\n");
        await f.CommitAsync("Decoy", "x.txt");
        await f.WriteAsync("x.txt", "decoy\nchanged\n");
        await f.WriteAsync("0.txt", "first\n");
        await f.WriteAsync(name, "hostile\n");

        var refused = await new GitWriteRouter(f.Runner).ExecuteAsync(new StashRequest(await f.ReadAsync(), new("with untracked files", IncludeUntracked: true), StashAction.Push), f.Token);

        Assert.Equal(GitWriteOutcome.Rejected, refused.Outcome);
        Assert.Equal(StashOutcomeText.UntrackedNotIndexable, refused.Message);
        Assert.Contains("'" + name + "'", refused.Details);
        Assert.DoesNotContain("0.txt", refused.Details);
        Assert.Equal("hostile\n", await File.ReadAllTextAsync(Path.Combine(f.Root, name), f.Token));
        Assert.Equal("decoy\nchanged\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "x.txt"), f.Token));
        Assert.Equal("", await f.GitAsync("stash", "list"));
    }

    // Git lists a repository inside the working tree as one untracked folder, which a stash with untracked files leaves
    // in place; one without a commit yet is no file Git cannot keep, so it does not refuse the stash.
    [Fact]
    public async Task WhenAnUntrackedFolderIsARepositoryWithoutACommit_ShouldStashWithUntrackedFilesAndLeaveItInPlace()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("x.txt", "decoy\n");
        await f.CommitAsync("Decoy", "x.txt");
        await f.WriteAsync("x.txt", "decoy\nchanged\n");
        var inner = Path.Combine(f.Root, "inner");
        await f.GitAtAsync(f.Root, "init", "-q", inner);
        await File.WriteAllTextAsync(Path.Combine(inner, "y.txt"), "inner\n", f.Token);

        var pushed = await new GitWriteRouter(f.Runner).ExecuteAsync(new StashRequest(await f.ReadAsync(), new("with untracked files", IncludeUntracked: true), StashAction.Push), f.Token);

        Assert.True(pushed.Outcome == GitWriteOutcome.Succeeded, pushed.Message + pushed.Details);
        Assert.Equal("inner\n", await File.ReadAllTextAsync(Path.Combine(inner, "y.txt"), f.Token));
        Assert.Equal("decoy\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "x.txt"), f.Token));
    }

    // Trag hands Git a stash's message in UTF-8: a commit encoding the user set for other tools must not label those bytes
    // as another encoding, and the stash list reads the message in UTF-8 whatever encoding Git is set to print in.
    [Theory]
    [InlineData("i18n.commitEncoding")]
    [InlineData("i18n.logOutputEncoding")]
    public async Task WhenAnEncodingIsSetToCp1250AndAStashGetsANonAsciiMessage_ShouldWriteAndListEveryLetter(string setting)
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("f.txt", "a\n");
        await f.CommitAsync("Base", "f.txt");
        await f.GitAsync("config", setting, "cp1250");
        await f.WriteAsync("f.txt", "b\n");

        var pushed = await new GitWriteRouter(f.Runner).ExecuteAsync(new StashRequest(await f.ReadAsync(), new("Čvor đak"), StashAction.Push), f.Token);

        Assert.True(pushed.Outcome == GitWriteOutcome.Succeeded, pushed.Message + pushed.Details);
        Assert.Equal("On main: Čvor đak", await f.GitAsync("log", "-1", "--encoding=UTF-8", "--format=%s", "refs/stash"));
        Assert.Equal("On main: Čvor đak", Assert.Single(await new CliStashReader(f.Runner).ReadStashesAsync(f.Root, f.Token)).Message);
    }

    // A stash without a message gets the subject of the commit it was made on, which Git prints in i18n.logOutputEncoding
    // and commits as UTF-8: under both encodings set to cp1250 its letters are kept.
    [Fact]
    public async Task WhenBothEncodingsAreCp1250AndAStashGetsNoMessage_ShouldKeepTheLettersOfTheSubjectItNames()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("f.txt", "a\n");
        await f.CommitAsync("Čvor đak", "f.txt");
        await f.GitAsync("config", "i18n.commitEncoding", "cp1250");
        await f.GitAsync("config", "i18n.logOutputEncoding", "cp1250");
        await f.WriteAsync("f.txt", "b\n");

        var pushed = await new GitWriteRouter(f.Runner).ExecuteAsync(new StashRequest(await f.ReadAsync(), new(""), StashAction.Push), f.Token);

        Assert.True(pushed.Outcome == GitWriteOutcome.Succeeded, pushed.Message + pushed.Details);
        Assert.EndsWith(" Čvor đak", await f.GitAsync("log", "-1", "--encoding=UTF-8", "--format=%s", "refs/stash"));
        Assert.EndsWith(" Čvor đak", Assert.Single(await new CliStashReader(f.Runner).ReadStashesAsync(f.Root, f.Token)).Message);
    }

    // An untracked file shown in the list can be gone before the click, as an editor's lock file goes when the document
    // closes; there is nothing of it to lose, so the stash with untracked files goes ahead with the others.
    [Fact]
    public async Task WhenAShownUntrackedFileIsGoneBeforeTheStash_ShouldStashWithUntrackedFiles()
    {
        using var f = await GitFixture.CreateAsync();
        await f.WriteAsync("x.txt", "decoy\n");
        await f.CommitAsync("Decoy", "x.txt");
        await f.WriteAsync("x.txt", "decoy\nchanged\n");
        await f.WriteAsync("keep.txt", "keep\n");
        await f.WriteAsync("~$lock.docx", "lock\n");
        var shown = await f.ReadAsync();
        File.Delete(Path.Combine(f.Root, "~$lock.docx"));

        var pushed = await new GitWriteRouter(f.Runner).ExecuteAsync(new StashRequest(shown, new("with untracked files", IncludeUntracked: true), StashAction.Push), f.Token);

        Assert.True(pushed.Outcome == GitWriteOutcome.Succeeded, pushed.Message + pushed.Details);
        Assert.Equal("keep.txt", await f.GitAsync("ls-tree", "-r", "--name-only", "stash@{0}^3"));
    }

    // The check before a stash with untracked files takes the index lock as the stash does, so a lock another program
    // holds is reported as a locked index, not as a file Git cannot keep.
    [Fact]
    public async Task WhenAnotherProgramHoldsTheIndexLock_ShouldRefuseToStashWithUntrackedFilesAndSayTheIndexIsLocked()
    {
        using var f = await GitFixture.CreateAsync();
        await f.WriteAsync("x.txt", "decoy\n");
        await f.CommitAsync("Decoy", "x.txt");
        await f.WriteAsync("x.txt", "decoy\nchanged\n");
        await f.WriteAsync("keep.txt", "keep\n");
        var shown = await f.ReadAsync();
        await File.WriteAllTextAsync(Path.Combine(f.Root, ".git", "index.lock"), "", f.Token);

        var refused = await new GitWriteRouter(f.Runner).ExecuteAsync(new StashRequest(shown, new("with untracked files", IncludeUntracked: true), StashAction.Push), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, WriteText.IndexLocked), (refused.Outcome, refused.Message));
        Assert.Equal("keep\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "keep.txt"), f.Token));
        Assert.Equal("", await f.GitAsync("stash", "list"));
    }

    // Git cannot read an untracked file another program holds open without sharing, and says it cannot index it too, but
    // then its own stash refuses and deletes nothing: Trag gives Git's reason, not the claim that stashing would delete it.
    [Fact]
    public async Task WhenAnotherProgramHoldsAnUntrackedFileOpen_ShouldRefuseToStashWithGitsOwnReason()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = await GitFixture.CreateAsync();
        await f.WriteAsync("x.txt", "decoy\n");
        await f.CommitAsync("Decoy", "x.txt");
        await f.WriteAsync("x.txt", "decoy\nchanged\n");
        await f.WriteAsync("held.txt", "held\n");
        var shown = await f.ReadAsync();

        GitWriteResult refused;
        using (new FileStream(Path.Combine(f.Root, "held.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            refused = await new GitWriteRouter(f.Runner).ExecuteAsync(new StashRequest(shown, new("with untracked files", IncludeUntracked: true), StashAction.Push), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, StashOutcomeText.Failed), (refused.Outcome, refused.Message));
        Assert.Contains("Permission denied", refused.Details);
        Assert.Equal("held\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "held.txt"), f.Token));
    }

    [Fact]
    public async Task WhenAnAppliedStashConflicts_ShouldReportADecisionAndKeepTheEntry()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        var stashes = new CliStashReader(new());
        var shared = Path.Combine(f.One, "shared.txt");
        var original = await File.ReadAllTextAsync(shared, f.Token);
        await File.WriteAllTextAsync(shared, original + "stashed line\n", f.Token);
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var pushed = await writer.ExecuteAsync(new StashRequest(snapshot, new("conflict candidate"), StashAction.Push), f.Token);
        Assert.True(pushed.Outcome == GitWriteOutcome.Succeeded, pushed.Message + pushed.Details);
        await File.WriteAllTextAsync(shared, original + "committed line\n", f.Token); await f.Git(f.One, "commit", "-am", "diverge");
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var candidate = Assert.Single(await stashes.ReadStashesAsync(f.One, f.Token));
        var conflict = await writer.ExecuteAsync(new StashRequest(snapshot, new(Id: candidate.Id), StashAction.Apply), f.Token);
        Assert.Equal(GitWriteOutcome.NeedsResolution, conflict.Outcome); Assert.Contains("conflicts", conflict.Message);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Contains(snapshot.Files, file => file.IsConflict); Assert.True(snapshot.HasUnsupportedWriteState);
        Assert.Single(await stashes.ReadStashesAsync(f.One, f.Token));
        await f.Git(f.One, "reset", "--hard");
    }
}
