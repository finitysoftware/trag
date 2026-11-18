using Trag.TestSupport;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Trag.Domain.Errors;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Setup;
using Trag.Infrastructure.Git.Write;

namespace Trag.IntegrationTests.Git.Read;

public sealed class RepositoryOpeningTests
{
    [Fact]
    public async Task WhenAnUnbornRepositoryIsOpenedFromASubfolder_ShouldFindItsRootAndBranchAndNeverWriteTheIndexOnARefresh()
    {
        using var fixture = await GitFixture.CreateAsync();
        var subfolder = Directory.CreateDirectory(Path.Combine(fixture.Root, "nested č")).FullName;
        await File.WriteAllTextAsync(Path.Combine(subfolder, "-new.txt"), "new\n");
        var snapshot = await fixture.ReadAsync(subfolder);
        Assert.Equal(Path.GetFullPath(fixture.Root), Path.GetFullPath(snapshot.Root));
        Assert.Equal("fixture", snapshot.Branch);
        Assert.True(snapshot.IsUnborn);
        Assert.False(snapshot.IsDetached);
        Assert.Null(snapshot.Upstream);
        Assert.False(Assert.Single(snapshot.Files).IsStaged);
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".git", "index")));
        await fixture.GitAsync("add", "--", "nested č/-new.txt");
        var index = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, ".git", "index"));
        Assert.True(Assert.Single((await fixture.ReadAsync()).Files).IsStaged);
        Assert.Equal(index, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, ".git", "index")));
    }

    // A .gitconfig saved by an editor in the system's ANSI code page holds a name that is not UTF-8, here "Petrović" in
    // Windows-1250. Git commits its bytes as they are; the repository opens, shows the name as far as it can be read, and
    // commits.
    [Fact]
    public async Task WhenTheUserNameIsNotUtf8_ShouldOpenShowTheNameAsFarAsItReadsAndCommit()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "--unset", "user.name");
        await File.AppendAllTextAsync(Path.Combine(f.Root, ".git", "config"), "[user]\n\tname = Marko Petrovi", f.Token);
        await File.AppendAllBytesAsync(Path.Combine(f.Root, ".git", "config"), [0xE6, (byte)'\n'], f.Token);
        await f.WriteAsync("a.txt", "a\n"); await f.GitAsync("add", "a.txt");

        var snapshot = await f.ReadAsync();
        var committed = await new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(snapshot, "First"), f.Token);

        Assert.Equal("Marko Petrovi�", snapshot.Author!.Name);
        Assert.True(committed.Outcome == GitWriteOutcome.Succeeded, committed.Message + committed.Details);
    }

    // The same editor saving .git/config leaves a remote's address that is not UTF-8; the repository still opens with its
    // remote, the address shown as far as it reads.
    [Fact]
    public async Task WhenARemoteAddressIsNotUtf8_ShouldStillOpenWithTheRemote()
    {
        using var f = await GitFixture.CreateAsync("main");
        await File.AppendAllTextAsync(Path.Combine(f.Root, ".git", "config"), "[remote \"origin\"]\n\turl = https://example.invalid/Ra", f.Token);
        await File.AppendAllBytesAsync(Path.Combine(f.Root, ".git", "config"), [0xE8, .. "un.git\n"u8], f.Token);

        var snapshot = await f.ReadAsync();

        Assert.Equal("origin", Assert.Single(snapshot.Remotes).Name);
    }

    // Git shortens such a branch to heads/<name> wherever it shortens loosely; the status names it plainly.
    [Fact]
    public async Task WhenBranchesShareTheirNamesWithTags_ShouldOpenOnTheBranchCommitOnItAndSwitchToTheOther()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("Initial");
        await f.GitAsync("branch", "topic");
        await f.GitAsync("tag", "main");
        await f.GitAsync("tag", "topic");
        await f.WriteAsync("a.txt", "a\n");
        await f.GitAsync("add", "--", "a.txt");
        var writer = new GitWriteRouter(new());

        var opened = await f.ReadAsync();
        var committed = await writer.ExecuteAsync(new CommitRequest(opened, "On main"), f.Token);
        var switched = await writer.ExecuteAsync(new BranchRequest(await f.ReadAsync(), new("topic"), BranchAction.Switch), f.Token);

        Assert.Equal((("main", GitWriteOutcome.Succeeded), GitWriteOutcome.Succeeded, "topic"),
            ((opened.Branch, committed.Outcome), switched.Outcome, (await f.ReadAsync()).Branch));
    }

    [Fact]
    public async Task WhenFilesArePartlyStagedRenamedAndUntracked_ShouldReportEachAsItIsWithoutTouchingTheIndexOrTheFiles()
    {
        using var fixture = await GitFixture.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "space č.txt"), "first\n");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "old.txt"), "rename\n");
        await fixture.GitAsync("add", "--", "space č.txt", "old.txt");
        await fixture.GitAsync("commit", "-m", "Initial fixture");
        await File.AppendAllTextAsync(Path.Combine(fixture.Root, "space č.txt"), "staged\n");
        await fixture.GitAsync("add", "--", "space č.txt");
        await File.AppendAllTextAsync(Path.Combine(fixture.Root, "space č.txt"), "unstaged\n");
        await fixture.GitAsync("mv", "--", "old.txt", "renamed č.txt");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "-untracked.txt"), "new\n");
        var indexPath = Path.Combine(fixture.Root, ".git", "index");
        var indexBefore = await File.ReadAllBytesAsync(indexPath);
        var contentBefore = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "space č.txt"));
        var snapshot = await fixture.ReadAsync();
        var partial = Assert.Single(snapshot.Files, file => file.Path.Display == "space č.txt");
        Assert.True(partial.IsStaged && partial.IsUnstaged);
        Assert.Equal("old.txt", Assert.Single(snapshot.Files, file => file.Index == 'R').OriginalPath!.Display);
        Assert.Contains(snapshot.Files, file => file.Path.Display == "-untracked.txt" && file.Worktree == '?');
        Assert.Equal(indexBefore, await File.ReadAllBytesAsync(indexPath));
        Assert.Equal(contentBefore, await File.ReadAllTextAsync(Path.Combine(fixture.Root, "space č.txt")));
    }

    [Fact]
    public async Task WhenTheBranchTracksANeverFetchedBranchOfARemoteWithDotsInItsName_ShouldReadTheConfiguredRemoteAndUpstream()
    {
        using var fixture = await GitFixture.CreateAsync();
        await ConfigureTeamRemoteAsync(fixture);
        var snapshot = await fixture.ReadAsync();
        Assert.Equal(("team.with.dots", "refs/heads/main"), (snapshot.TrackingRemote, snapshot.UpstreamBranchRef)); Assert.Null(snapshot.Upstream);
    }

    [Fact]
    public async Task WhenTheRemoteUrlsAreRewrittenAndCarryCredentials_ShouldShowTheEffectiveUrlsWithoutTheCredentials()
    {
        using var fixture = await GitFixture.CreateAsync();
        await ConfigureTeamRemoteAsync(fixture);
        var snapshot = await fixture.ReadAsync();
        var remote = Assert.Single(snapshot.Remotes);
        Assert.Equal("https://example.invalid/team/repo.git", remote.FetchAddress);
        Assert.Equal("https://push.invalid/team/repo.git", remote.PushAddress);
        Assert.DoesNotContain("synthetic", remote.ToString());
    }

    [Fact]
    public async Task WhenHeadIsDetachedOnACleanWorkingTree_ShouldReportItDetachedAndUnsupportedWithNoFiles()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("commit", "--allow-empty", "-m", "Initial fixture");
        await fixture.GitAsync("checkout", "--detach", "HEAD");
        var snapshot = await fixture.ReadAsync();
        Assert.True(snapshot.IsDetached);
        Assert.False(snapshot.IsUnborn);
        Assert.True(snapshot.HasUnsupportedState);
        Assert.Empty(snapshot.Files);
    }

    [Theory]
    [InlineData("merge", RepositoryOperation.Merge)]
    [InlineData("rebase", RepositoryOperation.Rebase)]
    [InlineData("cherry-pick", RepositoryOperation.CherryPick)]
    public async Task WhenAMergeRebaseOrCherryPickStartedOutsideTragStopsOnAConflict_ShouldRecognizeTheOperationAndTheConflict(string command, RepositoryOperation operation)
    {
        using var fixture = await GitFixture.CreateAsync();
        var file = Path.Combine(fixture.Root, "conflict.txt");
        await File.WriteAllTextAsync(file, "base\n");
        await fixture.GitAsync("add", "--", "conflict.txt");
        await fixture.GitAsync("commit", "-m", "Base");
        await fixture.GitAsync("checkout", "-b", "other");
        await File.WriteAllTextAsync(file, "other\n");
        await fixture.GitAsync("add", "--", "conflict.txt");
        await fixture.GitAsync("commit", "-m", "Other");
        await fixture.GitAsync("checkout", "fixture");
        await File.WriteAllTextAsync(file, "local\n");
        await fixture.GitAsync("add", "--", "conflict.txt");
        await fixture.GitAsync("commit", "-m", "Local");
        Assert.NotEqual(0, (await fixture.RunAsync("-c", "merge.ff=true", command, "other")).ExitCode);
        var snapshot = await fixture.ReadAsync();
        Assert.Equal(operation, snapshot.Operation);
        var conflict = Assert.Single(snapshot.Files);
        Assert.True(conflict.IsConflict);
        Assert.False(conflict.IsStaged);
        Assert.True(conflict.IsUnstaged);
    }

    [Fact]
    public async Task WhenTheFolderIsNoRepositoryABareOneOrGone_ShouldRefuseItWithItsOwnError()
    {
        using var fixture = await GitFixture.CreateAsync();
        var reader = new CliRepositoryReader(new GitProcessRunner());
        Assert.Equal(RepositoryError.NotRepository, (await Assert.ThrowsAsync<RepositoryException>(() => reader.ReadAsync(fixture.Container, fixture.Token))).Error);
        var bare = Directory.CreateDirectory(Path.Combine(fixture.Container, "bare")).FullName;
        Assert.Equal(0, (await new GitProcessRunner().RunAsync(bare, ["init", "--bare"], fixture.Token)).ExitCode);
        Assert.Equal(RepositoryError.BareRepository, (await Assert.ThrowsAsync<RepositoryException>(() => reader.ReadAsync(bare, fixture.Token))).Error);
        Assert.Equal(RepositoryError.PathUnavailable, (await Assert.ThrowsAsync<RepositoryException>(() => reader.ReadAsync(Path.Combine(fixture.Container, "missing"), fixture.Token))).Error);
    }

    // A Git older than the one Trag works with is refused before anything of the repository is read.
    [Fact]
    public async Task WhenGitIsTooOld_ShouldReportGitTooOld()
    {
        using var fixture = await GitFixture.CreateAsync();
        var old = await TestProgram.WriteAsync(fixture.Container, "old-git", "@echo git version 2.40.0\r\n", "echo git version 2.40.0\n");
        var reader = new CliRepositoryReader(new GitProcessRunner(executable: new() { Path = old }));

        var error = await Assert.ThrowsAsync<RepositoryException>(() => reader.ReadAsync(fixture.Root, fixture.Token));

        Assert.Equal(RepositoryError.GitTooOld, error.Error);
    }

    // A repository folder the user may not look into, as one copied with another account's permissions: Git cannot see its
    // .git and says it is no repository, which sends the user to choose another folder. Trag says it cannot read it.
    [Fact]
    public async Task WhenTheFolderCannotBeRead_ShouldSayGitCouldNotReadItNotThatItIsNoRepository()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = await GitFixture.CreateAsync();
        var folder = new DirectoryInfo(fixture.Root);
        var denied = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory | FileSystemRights.Traverse,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny);
        var security = folder.GetAccessControl();
        security.AddAccessRule(denied);
        folder.SetAccessControl(security);
        RepositoryException error;
        try { error = await Assert.ThrowsAsync<RepositoryException>(() => fixture.ReadAsync()); }
        finally
        {
            security.RemoveAccessRule(denied);
            folder.SetAccessControl(security);
        }

        Assert.Equal(RepositoryError.ReadFailed, error.Error);
    }

    // Commits arriving all through the read make it read again; one that never settles ends in its own error, never in
    // another exception or a snapshot that mixes two states.
    [Fact]
    public async Task WhenTheRepositoryKeepsMovingDuringTheRead_ShouldOpenOrReportChangedDuringRead()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "a\n");
        await fixture.CommitAsync("first", "a.txt");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        var moving = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await fixture.RunAsync("commit", "--allow-empty", "-m", "moving");
                await Task.Delay(50);
            }
        });

        Exception? failure = null;
        RepositorySnapshot? snapshot = null;
        try { snapshot = await fixture.ReadAsync(); }
        catch (Exception error) { failure = error; }
        await stop.CancelAsync();
        await moving;

        Assert.True(snapshot is not null || failure is RepositoryException { Error: RepositoryError.ChangedDuringRead }, failure?.ToString());
    }

    // A revert whose REVERT_HEAD does not name a commit is still a revert in progress, but none Trag can finish: the
    // repository opens and shows it as unsupported.
    [Fact]
    public async Task WhenTheRevertHeadIsGarbage_ShouldShowTheRevertInProgressAsUnsupported()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "a\n");
        await fixture.CommitAsync("first", "a.txt");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, ".git", "REVERT_HEAD"), "not a commit\n");

        var snapshot = await fixture.ReadAsync();

        Assert.Equal(RepositoryOperation.Revert, snapshot.Operation);
        Assert.Null(snapshot.PickHead);
        Assert.False(snapshot.HasSupportedPick);
        Assert.True(snapshot.HasUnsupportedWriteState);
    }

    // A bisect started outside Trag is shown, and no write starts while it runs.
    [Fact]
    public async Task WhenABisectIsInProgress_ShouldShowTheOperationAndRefuseWrites()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "a\n");
        await fixture.CommitAsync("first", "a.txt");
        await fixture.WriteAsync("a.txt", "b\n");
        await fixture.CommitAsync("second", "a.txt");
        await fixture.GitAsync("bisect", "start");
        await fixture.WriteAsync("a.txt", "changed\n");
        var snapshot = await fixture.ReadAsync();

        var refused = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new StageRequest(snapshot, [snapshot.Files.Single().Path]), fixture.Token);

        Assert.Equal(RepositoryOperation.Bisect, snapshot.Operation);
        Assert.Equal((GitWriteOutcome.Rejected, Trag.Domain.Presentation.Text.WriteText.StateUnsupported), (refused.Outcome, refused.Message));
    }

    // A rebase stopped outside Trag whose state files hold bytes that are not UTF-8 still opens: the names and the message
    // are text to show, never a reason the repository cannot be read.
    [Fact]
    public async Task WhenRebaseStateFilesAreNotUtf8_ShouldStillOpen()
    {
        using var fixture = await GitFixture.CreateAsync();
        var file = Path.Combine(fixture.Root, "conflict.txt");
        await File.WriteAllTextAsync(file, "base\n");
        await fixture.GitAsync("add", "--", "conflict.txt");
        await fixture.GitAsync("commit", "-m", "Base");
        await fixture.GitAsync("checkout", "-b", "other");
        await File.WriteAllTextAsync(file, "other\n");
        await fixture.GitAsync("commit", "-am", "Other");
        await fixture.GitAsync("checkout", "fixture");
        await File.WriteAllTextAsync(file, "local\n");
        await fixture.GitAsync("commit", "-am", "Local");
        Assert.NotEqual(0, (await fixture.RunAsync("rebase", "other")).ExitCode);
        var state = Path.Combine(fixture.Root, ".git", "rebase-merge");
        await File.WriteAllBytesAsync(Path.Combine(state, "message"), [0x4C, 0x6F, 0xFF, 0xFE, 0x63, 0x61, 0x6C, 0x0A]);

        var snapshot = await fixture.ReadAsync();

        Assert.Equal(RepositoryOperation.Rebase, snapshot.Operation);
        Assert.True(Assert.Single(snapshot.Files).IsConflict);
    }

    [Fact]
    public async Task WhenGitIsMissing_ShouldReportGitMissing()
    {
        using var fixture = await GitFixture.CreateAsync();
        var reader = new CliRepositoryReader(new GitProcessRunner(executable: new() { Path = Path.Combine(fixture.Container, "missing-git.exe") }));
        Assert.Equal(RepositoryError.GitMissing, (await Assert.ThrowsAsync<RepositoryException>(() => reader.ReadAsync(fixture.Root, fixture.Token))).Error);
    }

    // Git is asked its version once, and again only when another Git program runs, as after one is chosen in Settings, so
    // the version the status bar shows is that program's. Whether it runs git-lfs is asked the same way, as one without it
    // has Git LFS set up in vain.
    [Fact]
    public async Task WhenTheGitProgramChanges_ShouldAskTheNewOneItsVersion()
    {
        using var fixture = await GitFixture.CreateAsync();
        await LfsConfig.SetAsync(fixture, fixture.Root, installed: true);
        var executable = new GitExecutable();
        var started = 0;
        var reader = new CliRepositoryReader(new GitProcessRunner(exited: _ => Interlocked.Increment(ref started), executable: executable));
        await reader.ReadAsync(fixture.Root, fixture.Token);
        var before = started;
        await reader.ReadAsync(fixture.Root, fixture.Token);
        var sameProgram = started - before;
        executable.Path = (await new GitLocator(new GitExecutable()).FindAsync(null, fixture.Token)).Git!.Path;

        before = started;
        await reader.ReadAsync(fixture.Root, fixture.Token);

        Assert.Equal(sameProgram + 2, started - before);
    }

    [Fact]
    public async Task WhenTheReadIsCancelled_ShouldEndInCancellation()
    {
        using var fixture = await GitFixture.CreateAsync();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.ReadAsync(cancellationToken: canceled.Token));
    }

    [Fact]
    public async Task WhenTheRepositoryHasALinkedWorktreeOrASparseCheckout_ShouldMarkEachAsUnsupportedButNotAGitlink()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("commit", "--allow-empty", "-m", "Initial fixture");
        var linked = Path.Combine(fixture.Container, "linked");
        await fixture.GitAsync("worktree", "add", "-b", "linked", linked);
        Assert.Contains((await fixture.ReadAsync(linked)).Limitations, value => value.StartsWith("Linked worktree", StringComparison.Ordinal));
        await fixture.GitAsync("sparse-checkout", "init", "--cone");
        Assert.Contains((await fixture.ReadAsync()).Limitations, value => value.StartsWith("Sparse checkout", StringComparison.Ordinal));
        await fixture.GitAsync("sparse-checkout", "disable");
        var head = Encoding.UTF8.GetString((await fixture.RunAsync("rev-parse", "HEAD")).StandardOutput).Trim();
        await fixture.GitAsync("update-index", "--add", "--cacheinfo", $"160000,{head},module");
        Assert.Empty((await fixture.ReadAsync()).Limitations);
    }

    // Every remote's two addresses come from one git call, so a repository with more remotes costs no more to read.
    [Fact]
    public async Task WhenARepositoryHasMoreRemotes_ShouldReadItWithAsManyGitCalls()
    {
        using var fixture = await GitFixture.CreateAsync(); await fixture.CommitAsync("initial");
        await fixture.GitAsync("remote", "add", "origin", "https://example.invalid/origin.git");
        var calls = 0;
        var reader = new CliRepositoryReader(new GitProcessRunner(exited: _ => Interlocked.Increment(ref calls)));
        await reader.ReadAsync(fixture.Root, fixture.Token);
        Interlocked.Exchange(ref calls, 0); await reader.ReadAsync(fixture.Root, fixture.Token);
        var withOne = Interlocked.Exchange(ref calls, 0);
        await fixture.GitAsync("remote", "add", "second", "https://example.invalid/second.git");
        await fixture.GitAsync("remote", "add", "third", "https://example.invalid/third.git");
        var snapshot = await reader.ReadAsync(fixture.Root, fixture.Token);
        Assert.Equal(withOne, Interlocked.Exchange(ref calls, 0));
        Assert.Equal(["origin", "second", "third"], snapshot.Remotes.Select(remote => remote.Name));
    }

    // The addresses are the ones Git itself uses: insteadOf and pushInsteadOf applied, the first of several URLs, a push
    // URL of its own, and a local folder with a space in its path.
    [Fact]
    public async Task WhenRemotesRewriteOrRepeatTheirAddresses_ShouldReadTheAddressesGitUses()
    {
        using var fixture = await GitFixture.CreateAsync(); await fixture.CommitAsync("initial");
        await fixture.GitAsync("remote", "add", "many", "https://example.invalid/first.git");
        await fixture.GitAsync("config", "--add", "remote.many.url", "https://example.invalid/second.git");
        await fixture.GitAsync("config", "remote.many.pushurl", "https://push.invalid/first.git");
        await fixture.GitAsync("config", "--add", "remote.many.pushurl", "https://push.invalid/second.git");
        await fixture.GitAsync("remote", "add", "local", Path.Combine(fixture.Container, "a folder", "repo"));
        await fixture.GitAsync("config", "url.https://rewritten.invalid/.insteadOf", "short:");
        await fixture.GitAsync("config", "url.ssh://push.invalid/.pushInsteadOf", "https://example.invalid/");
        await fixture.GitAsync("remote", "add", "short", "short:repo.git");
        await fixture.GitAsync("remote", "add", "plain", "https://example.invalid/plain.git");
        var remotes = (await fixture.ReadAsync()).Remotes;
        Assert.Equal(["local", "many", "plain", "short"], remotes.Select(remote => remote.Name));
        foreach (var remote in remotes)
        {
            Assert.Equal(await fixture.GitAsync("remote", "get-url", "--", remote.Name), remote.FetchAddress);
            Assert.Equal(await fixture.GitAsync("remote", "get-url", "--push", "--", remote.Name), remote.PushAddress);
        }
    }

    // An address with a line break spans two lines of remote -v, so that remote is asked by name.
    [Fact]
    public async Task WhenARemoteAddressHoldsALineBreak_ShouldStillReadItOnOneLine()
    {
        using var fixture = await GitFixture.CreateAsync(); await fixture.CommitAsync("initial");
        await fixture.GitAsync("config", "remote.odd.url", "first\nsecond");
        var remote = Assert.Single((await fixture.ReadAsync()).Remotes);
        Assert.Equal(("first second", "first second"), (remote.FetchAddress, remote.PushAddress));
    }

    // The remote team.with.dots, its fetch URL rewritten through insteadOf and both URLs carrying credentials, and the
    // current branch tracking its main, which was never fetched.
    private static async Task ConfigureTeamRemoteAsync(GitFixture fixture)
    {
        await fixture.GitAsync("config", "remote.team.with.dots.url", "fixture:team/repo.git");
        await fixture.GitAsync("config", "url.https://user:synthetic@example.invalid/.insteadOf", "fixture:");
        await fixture.GitAsync("config", "remote.team.with.dots.pushurl", "https://user:synthetic@push.invalid/team/repo.git?token=fixture");
        await fixture.GitAsync("config", "branch.fixture.remote", "team.with.dots");
        await fixture.GitAsync("config", "branch.fixture.merge", "refs/heads/main");
    }
}
