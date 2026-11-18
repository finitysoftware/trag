using System.Diagnostics;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.IntegrationTests.Git.Sync;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// A commit Git cannot sign is refused in Trag's words, and the repository stays as it was: the signing program missing, a
// key that is not there, or an SSH key with a passphrase that only a terminal could ask for, which Git refuses at once
// instead of waiting.
public sealed class SigningFailureTests
{
    // One commit made unsigned, then commits signed with SSH by the key the caller sets up, and a change to a.txt staged.
    private static async Task<GitFixture> SigningAsync(Func<GitFixture, Task> key)
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            await f.WriteAsync("a.txt", "one\n");
            await f.CommitAsync("one", "a.txt");
            await f.GitAsync("config", "gpg.format", "ssh");
            await key(f);
            await f.GitAsync("config", "commit.gpgsign", "true");
            await f.WriteAsync("a.txt", "two\n");
            await f.GitAsync("add", "a.txt");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private static Task<GitFixture> MissingProgramAsync() => SigningAsync(MissingProgram);

    private static Task<GitFixture> MissingKeyAsync() => SigningAsync(MissingKey);

    private static async Task MissingProgram(GitFixture f)
    {
        await f.GitAsync("config", "gpg.ssh.program", Path.Combine(f.Container, "missing-keygen"));
        await f.GitAsync("config", "user.signingkey", Path.Combine(f.Container, "key.pub"));
    }

    // The ssh-keygen on the PATH, with a key that is not there.
    private static async Task MissingKey(GitFixture f)
    {
        await f.GitAsync("config", "gpg.ssh.program", OnPath("ssh-keygen"));
        await f.GitAsync("config", "user.signingkey", Path.Combine(f.Container, "signing-key.pub"));
    }

    // The key the configuration names appears where it says, as the user fixes it.
    private static async Task MakeKeyAsync(GitFixture f) =>
        await KeyAsync(OnPath("ssh-keygen"), (await f.GitAsync("config", "user.signingkey"))[..^".pub".Length], "", f.Token);

    private static Task<GitFixture> LockedKeyAsync(string keygen) => SigningAsync(async f =>
    {
        var key = Path.Combine(f.Container, "locked-key");
        await KeyAsync(keygen, key, "a passphrase", f.Token);
        await f.GitAsync("config", "gpg.ssh.program", keygen);
        await f.GitAsync("config", "user.signingkey", key + ".pub");
    });

    private static async Task KeyAsync(string keygen, string key, string passphrase, CancellationToken ct)
    {
        using var made = Process.Start(new ProcessStartInfo(keygen) { ArgumentList = { "-q", "-t", "ed25519", "-N", passphrase, "-f", key }, UseShellExecute = false })!;
        await made.WaitForExitAsync(ct);
    }

    private static string OnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .SelectMany(folder => new[] { Path.Combine(folder, program + ".exe"), Path.Combine(folder, program) })
            .First(File.Exists);

    // OpenPGP with a key the keyring does not hold: gpg refuses at once, and the refusal says the commit was not signed
    // with OpenPGP. The keyring is a fresh one of the test's, never the user's.
    [Fact]
    public async Task WhenGpgHasNoSecretKey_ShouldRefuseWithTheGpgAdvice()
    {
        var gpg = OnPath("gpg");
        var f = await GitFixture.CreateAsync("main");
        using var _ = f;
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("a.txt", "one\n");
        await f.CommitAsync("one", "a.txt");
        var home = Directory.CreateDirectory(Path.Combine(f.Container, "gnupg")).FullName;
        var wrapper = Path.Combine(f.Container, "gpg-fresh-home.sh");
        await File.WriteAllTextAsync(wrapper, "#!/bin/sh\nexec \"" + gpg.Replace('\\', '/') + "\" --homedir \"" + home.Replace('\\', '/') + "\" --batch --no-tty \"$@\"\n", f.Token);
        await f.GitAsync("config", "gpg.format", "openpgp");
        await f.GitAsync("config", "gpg.program", wrapper.Replace('\\', '/'));
        await f.GitAsync("config", "user.signingkey", "DEADBEEFDEADBEEF");
        await f.GitAsync("config", "commit.gpgsign", "true");
        await f.WriteAsync("a.txt", "two\n");
        await f.GitAsync("add", "a.txt");
        var head = await f.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(await f.ReadAsync(), "two"), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, CommitOutcomeText.NotSigned(CommitSigning.OpenPgp)), (result.Outcome, result.Message));
        Assert.Equal((head, "a.txt"), (await f.GitAsync("rev-parse", "HEAD"), await f.GitAsync("diff", "--cached", "--name-only")));
    }

    // Git knows gpg.format only in lower case and refuses a signed commit under any other value before it signs anything:
    // the refusal is Git's own, not advice for a key of the kind Trag would guess, and what is staged stays.
    [Theory]
    [InlineData("SSH")]
    [InlineData("pgp")]
    public async Task WhenGpgFormatIsUnknownOrUpperCase_ShouldRefuseWithGitsReasonAndKeepWhatIsStaged(string format)
    {
        using var f = await SigningAsync(MissingKey);
        await f.GitAsync("config", "gpg.format", format);
        var head = await f.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(await f.ReadAsync(), "two"), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, CommitOutcomeText.Rejected), (result.Outcome, result.Message));
        Assert.Contains("invalid value for 'gpg.format': '" + format + "'", result.Details);
        Assert.Equal((head, "a.txt"), (await f.GitAsync("rev-parse", "HEAD"), await f.GitAsync("diff", "--cached", "--name-only")));
    }

    // X.509 with a certificate gpgsm does not hold: the refusal names the certificate. The keybox is a fresh one of the
    // test's, never the user's.
    [Fact]
    public async Task WhenGpgsmHasNoCertificate_ShouldRefuseWithTheX509Advice()
    {
        var gpgsm = OnPath("gpgsm");
        using var f = await SigningAsync(async f =>
        {
            var home = Directory.CreateDirectory(Path.Combine(f.Container, "gnupg")).FullName;
            var wrapper = Path.Combine(f.Container, "gpgsm-fresh-home.sh");
            await File.WriteAllTextAsync(wrapper, "#!/bin/sh\nexec \"" + gpgsm.Replace('\\', '/') + "\" --homedir \"" + home.Replace('\\', '/') + "\" --batch \"$@\"\n", f.Token);
            await f.GitAsync("config", "gpg.format", "x509");
            await f.GitAsync("config", "gpg.x509.program", wrapper.Replace('\\', '/'));
            await f.GitAsync("config", "user.signingkey", "0xDEADBEEF");
        });
        var head = await f.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(await f.ReadAsync(), "two"), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, CommitOutcomeText.NotSigned(CommitSigning.X509)), (result.Outcome, result.Message));
        Assert.Equal((head, "a.txt"), (await f.GitAsync("rev-parse", "HEAD"), await f.GitAsync("diff", "--cached", "--name-only")));
    }

    // Git signs annotated tags here while it does not sign commits; the refusal names the key gpg.format chose.
    [Fact]
    public async Task WhenATagCannotBeSigned_ShouldRefuseItInTragsWordsAndCreateNoTag()
    {
        using var f = await MissingProgramAsync();
        await f.GitAsync("config", "commit.gpgsign", "false");
        await f.GitAsync("config", "tag.gpgSign", "true");
        var head = await f.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new TagRequest(await f.ReadAsync(), new("v1", head, "Release")), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, TagOutcomeText.NotSigned(CommitSigning.Ssh)), (result.Outcome, result.Message));
        Assert.Equal("", await f.GitAsync("tag", "--list", "v1"));
    }

    [Fact]
    public async Task WhenAnAnnotatedTagIsForcedToBeSignedWithAMissingKey_ShouldRefuseItAndCreateNoTag()
    {
        using var f = await MissingKeyAsync();
        await f.GitAsync("config", "tag.forceSignAnnotated", "true");
        var head = await f.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new TagRequest(await f.ReadAsync(), new("v1", head, "Release")), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, TagOutcomeText.NotSigned(CommitSigning.Ssh)), (result.Outcome, result.Message));
        Assert.Equal("", await f.GitAsync("tag", "--list", "v1"));
    }

    [Fact]
    public async Task WhenTheSigningProgramCannotRun_ShouldRefuseTheCommitAndKeepWhatIsStaged()
    {
        using var f = await MissingProgramAsync();
        var head = await f.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(await f.ReadAsync(), "two"), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, CommitOutcomeText.NotSigned(CommitSigning.Ssh)), (result.Outcome, result.Message));
        Assert.Equal((head, "a.txt"), (await f.GitAsync("rev-parse", "HEAD"), await f.GitAsync("diff", "--cached", "--name-only")));
    }

    // The ssh-keygen on the PATH, and Windows' own, which reads a passphrase from the console every Git Trag starts has
    // hidden; each makes its own key, since Windows' refuses a key file whose permissions another one set.
    public static TheoryData<string> Keygens() =>
        [.. new[] { OnPath("ssh-keygen"), Path.Combine(Environment.SystemDirectory, "OpenSSH", "ssh-keygen.exe") }.Where(File.Exists).Distinct()];

    [Theory]
    [MemberData(nameof(Keygens))]
    public async Task WhenAnSshKeyAsksForAPassphrase_ShouldRefuseTheCommitWithoutWaiting(string keygen)
    {
        using var f = await LockedKeyAsync(keygen);

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(await f.ReadAsync(), "two"), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, CommitOutcomeText.NotSigned(CommitSigning.Ssh)), (result.Outcome, result.Message));
    }

    // main and feature each changed a different file since one; commits are then signed with SSH by the key the caller
    // sets up.
    private static async Task<(GitFixture F, string Feature)> DivergedAsync(Func<GitFixture, Task> key)
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            await f.WriteAsync("a.txt", "one\n");
            await f.WriteAsync("b.txt", "one\n");
            await f.CommitAsync("one");
            await f.GitAsync("switch", "-c", "feature");
            await f.WriteAsync("b.txt", "feature\n");
            var feature = await f.CommitAsync("feature", "b.txt");
            await f.GitAsync("switch", "main");
            await f.WriteAsync("a.txt", "main\n");
            await f.CommitAsync("main", "a.txt");
            await f.GitAsync("config", "gpg.format", "ssh");
            await key(f);
            await f.GitAsync("config", "commit.gpgsign", "true");
            return (f, feature);
        }
        catch { f.Dispose(); throw; }
    }

    // Git leaves the merge ready to finish; Trag says why it is not finished and keeps it, so Finish works once the key does.
    [Fact]
    public async Task WhenAMergeCommitCannotBeSigned_ShouldLeaveTheMergeReadyToFinishAndSayWhy()
    {
        var (f, feature) = await DivergedAsync(MissingProgram);
        using var _ = f;
        var head = await f.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new MergeRequest(await f.ReadAsync(), "feature", feature, false, "Merge branch 'feature'"), f.Token);

        var after = await f.ReadAsync();
        Assert.Equal((GitWriteOutcome.NeedsResolution, MergeOutcomeText.NotSigned(CommitSigning.Ssh)), (result.Outcome, result.Message));
        Assert.Equal((RepositoryOperation.Merge, 0, head), (after.Operation, after.Conflicts.Count(), after.Head));
    }

    [Fact]
    public async Task WhenACherryPickCannotBeSigned_ShouldLeaveItReadyToFinishAndSayWhy()
    {
        var (f, feature) = await DivergedAsync(MissingProgram);
        using var _ = f;
        var head = await f.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new CherryPickRequest(await f.ReadAsync(), feature), f.Token);

        var after = await f.ReadAsync();
        Assert.Equal((GitWriteOutcome.NeedsResolution, PickText.NotSigned(RepositoryOperation.CherryPick, CommitSigning.Ssh)), (result.Outcome, result.Message));
        Assert.Equal((RepositoryOperation.CherryPick, head), (after.Operation, after.Head));
    }

    // Git leaves nothing in progress for a revert it could not commit, only its changes staged; Trag puts them back.
    [Fact]
    public async Task WhenARevertCannotBeSigned_ShouldLeaveTheRepositoryAsItWasAndSayWhy()
    {
        var (f, _) = await DivergedAsync(MissingProgram);
        using var __ = f;
        var head = await f.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new RevertRequest(await f.ReadAsync(), head), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, PickText.NotSignedUndone(CommitSigning.Ssh)), (result.Outcome, result.Message));
        Assert.Equal((head, ""), (await f.GitAsync("rev-parse", "HEAD"), await f.GitAsync("status", "--porcelain")));
        Assert.Equal(RepositoryOperation.None, (await f.ReadAsync()).Operation);
    }

    [Fact]
    public async Task WhenARebaseCannotSignAReplayedCommit_ShouldStopItAndSayWhy()
    {
        var (f, feature) = await DivergedAsync(MissingProgram);
        using var _ = f;

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new RebaseRequest(await f.ReadAsync(), "feature", feature), f.Token);

        Assert.Equal((GitWriteOutcome.NeedsResolution, RebaseText.NotSigned(CommitSigning.Ssh)), (result.Outcome, result.Message));
        Assert.Equal(RepositoryOperation.Rebase, (await f.ReadAsync()).Operation);
    }

    // Finishing what the key could not sign is refused the same way, and the operation stays ready.
    [Fact]
    public async Task WhenAPickIsFinishedWhileTheKeyStillCannotSign_ShouldKeepItReadyAndSayWhy()
    {
        var (f, feature) = await DivergedAsync(MissingProgram);
        using var _ = f;
        var writer = new GitWriteRouter(f.Runner);
        await writer.ExecuteAsync(new CherryPickRequest(await f.ReadAsync(), feature), f.Token);

        var result = await writer.ExecuteAsync(new PickContinueRequest(await f.ReadAsync()), f.Token);

        Assert.Equal(PickText.NotSigned(RepositoryOperation.CherryPick, CommitSigning.Ssh), result.Message);
        Assert.Equal(RepositoryOperation.CherryPick, (await f.ReadAsync()).Operation);
    }

    [Fact]
    public async Task WhenARebaseIsContinuedWhileTheKeyStillCannotSign_ShouldKeepItStoppedAndSayWhy()
    {
        var (f, feature) = await DivergedAsync(MissingProgram);
        using var _ = f;
        var writer = new GitWriteRouter(f.Runner);
        await writer.ExecuteAsync(new RebaseRequest(await f.ReadAsync(), "feature", feature), f.Token);

        var result = await writer.ExecuteAsync(new RebaseContinueRequest(await f.ReadAsync(), "main"), f.Token);

        Assert.Equal(RebaseText.NotSigned(CommitSigning.Ssh), result.Message);
        Assert.Equal(RepositoryOperation.Rebase, (await f.ReadAsync()).Operation);
    }

    // Git leaves the replayed commit's changes staged with its message prepared. Continue, with the message the rebase
    // view takes from the stop, commits them as that commit, and a plain rebase drops the pick Git put back as already
    // applied.
    [Fact]
    public async Task WhenARebaseStoppedOnAnUnsignedCommitIsContinuedOnceTheKeyCanSign_ShouldCommitItWithItsOwnMessageSigned()
    {
        var (f, feature) = await DivergedAsync(MissingKey);
        using var _ = f;
        var writer = new GitWriteRouter(f.Runner);
        await writer.ExecuteAsync(new RebaseRequest(await f.ReadAsync(), "feature", feature), f.Token);
        await MakeKeyAsync(f);
        var stopped = await f.ReadAsync();

        var result = await writer.ExecuteAsync(new RebaseContinueRequest(stopped, MergeMessages.Clean(stopped.Rebase!.Message)), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("main\nfeature\none", await f.GitAsync("log", "--format=%s"));
        Assert.Matches(SignedCommitTests.Signature, await f.GitAsync("cat-file", "commit", "HEAD"));
    }

    [Fact]
    public async Task WhenAnAmendCannotBeSigned_ShouldRefuseItAndKeepTheLastCommit()
    {
        using var f = await MissingProgramAsync();
        var head = await f.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(await f.ReadAsync(), "one, amended", Amend: true), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, CommitOutcomeText.NotSigned(CommitSigning.Ssh)), (result.Outcome, result.Message));
        Assert.Equal(head, await f.GitAsync("rev-parse", "HEAD"));
    }

    // A pull's merge commit is refused as a merge's is: the merge stays ready to finish, with nothing to resolve.
    [Fact]
    public async Task WhenAPullMergeCommitCannotBeSigned_ShouldLeaveTheMergeReadyToFinishAndSayWhy()
    {
        using var f = await SyncFixture.Create();
        await MergeScenario.DivergeCleanly(f);
        await f.Git(f.One, "config", "gpg.format", "ssh");
        await f.Git(f.One, "config", "gpg.ssh.program", OnPath("ssh-keygen"));
        await f.Git(f.One, "config", "user.signingkey", Path.Combine(f.Root, "signing-key.pub"));
        await f.Git(f.One, "config", "commit.gpgsign", "true");
        var head = await f.Head(f.One);

        var result = await f.Execute(SyncAction.Pull);

        var after = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Equal((GitWriteOutcome.NeedsResolution, MergeOutcomeText.NotSigned(CommitSigning.Ssh)), (result.Outcome, result.Message));
        Assert.Equal((RepositoryOperation.Merge, 0, head), (after.Operation, after.Conflicts.Count(), after.Head));
    }

    // two and three are committed unsigned after one, while commits are signed with a key that is not there.
    private static async Task<GitFixture> TwoUnsignedCommitsAsync()
    {
        var f = await MissingKeyAsync();
        try
        {
            await f.GitAsync("-c", "commit.gpgsign=false", "commit", "-m", "two");
            await f.WriteAsync("b.txt", "three\n");
            await f.GitAsync("add", "b.txt");
            await f.GitAsync("-c", "commit.gpgsign=false", "commit", "-m", "three");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    // The rewrite rewords two and keeps three.
    private static async Task<GitWriteResult> RewordAsync(GitFixture f)
    {
        var (one, two, three) = (await f.GitAsync("rev-parse", "HEAD~2"), await f.GitAsync("rev-parse", "HEAD~1"), await f.GitAsync("rev-parse", "HEAD"));
        return await new GitWriteRouter(f.Runner).ExecuteAsync(new RewriteRequest(await f.ReadAsync(), one,
            [new(two, "two", "two", RewriteAction.Reword, "two, reworded"), new(three, "three", "three", RewriteAction.Pick, "three")]), f.Token);
    }

    [Fact]
    public async Task WhenARewordCannotBeSigned_ShouldStopTheRewriteAndSayWhy()
    {
        using var f = await TwoUnsignedCommitsAsync();

        var result = await RewordAsync(f);

        Assert.Equal((GitWriteOutcome.NeedsResolution, RebaseText.NotSigned(CommitSigning.Ssh)), (result.Outcome, result.Message));
        Assert.Equal(RepositoryOperation.Rebase, (await f.ReadAsync()).Operation);
    }

    // Continue runs the reword's amend again, so it stops there again while the key still cannot sign. Continue carries
    // a message, which Git has no commit to give at this stop.
    [Fact]
    public async Task WhenARewriteStoppedOnAnUnsignedRewordIsContinuedWhileTheKeyStillCannotSign_ShouldKeepItStoppedAndSayWhy()
    {
        using var f = await TwoUnsignedCommitsAsync();
        await RewordAsync(f);

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new RebaseContinueRequest(await f.ReadAsync(), "typed"), f.Token);

        Assert.Equal(RebaseText.NotSigned(CommitSigning.Ssh), result.Message);
        Assert.Equal(RepositoryOperation.Rebase, (await f.ReadAsync()).Operation);
    }

    [Fact]
    public async Task WhenARewriteStoppedOnAnUnsignedRewordIsContinuedOnceTheKeyCanSign_ShouldKeepTheNewMessageAndSignIt()
    {
        using var f = await TwoUnsignedCommitsAsync();
        await RewordAsync(f);
        await MakeKeyAsync(f);

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new RebaseContinueRequest(await f.ReadAsync(), "typed"), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("three\ntwo, reworded\none", await f.GitAsync("log", "--format=%s"));
        foreach (var commit in new[] { "HEAD", "HEAD~1" })
            Assert.Matches(SignedCommitTests.Signature, await f.GitAsync("cat-file", "commit", commit));
    }

    // The rewrite folds three into two, and the key cannot sign the fold.
    private static async Task FoldAsync(GitFixture f)
    {
        var (one, two, three) = (await f.GitAsync("rev-parse", "HEAD~2"), await f.GitAsync("rev-parse", "HEAD~1"), await f.GitAsync("rev-parse", "HEAD"));
        await new GitWriteRouter(f.Runner).ExecuteAsync(new RewriteRequest(await f.ReadAsync(), one,
            [new(two, "two", "two", RewriteAction.Pick, "two"), new(three, "three", "three", RewriteAction.Fixup, "three")]), f.Token);
    }

    // Git puts back the fold it could not commit and leaves its changes staged. The rebase view offers the message the
    // fold keeps, and Continue folds the changes into the commit before, with whatever was staged there too.
    [Fact]
    public async Task WhenARewriteStoppedOnAnUnsignedFoldIsContinuedOnceTheKeyCanSign_ShouldFoldItIntoItsCommit()
    {
        using var f = await TwoUnsignedCommitsAsync();
        await FoldAsync(f);
        await MakeKeyAsync(f);
        await f.WriteAsync("added.txt", "added at the stop\n");
        await f.GitAsync("add", "added.txt");
        await f.WriteAsync("untracked.txt", "left alone\n");
        var stopped = await f.ReadAsync();
        var message = MergeMessages.Clean(stopped.Rebase!.Message);

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new RebaseContinueRequest(stopped, message), f.Token);

        Assert.Equal("two", message);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("two\none", await f.GitAsync("log", "--format=%s"));
        Assert.Equal("added at the stop", await f.GitAsync("show", "HEAD:added.txt"));
        Assert.Matches(SignedCommitTests.Signature, await f.GitAsync("cat-file", "commit", "HEAD"));
    }

    [Fact]
    public async Task WhenARewriteStoppedOnAnUnsignedFoldIsContinuedWhileTheKeyStillCannotSign_ShouldKeepItStoppedAndSayWhy()
    {
        using var f = await TwoUnsignedCommitsAsync();
        await FoldAsync(f);
        var stopped = await f.ReadAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new RebaseContinueRequest(stopped, MergeMessages.Clean(stopped.Rebase!.Message)), f.Token);

        Assert.Equal(RebaseText.NotSigned(CommitSigning.Ssh), result.Message);
        var after = await f.ReadAsync();
        Assert.Equal((RepositoryOperation.Rebase, stopped.Head, stopped.Rebase.Step), (after.Operation, after.Head, after.Rebase!.Step));
    }

    // Git's skip of a fold it put back resets the fold's changes and then folds the commit again from the plan, so nothing
    // is left out: Skip is refused there, and Continue or Abort decide. From 2.56 Git stops to amend such a fold instead.
    [GitBeforeFact(2, 56)]
    public async Task WhenAFoldGitCouldNotCommitIsSkipped_ShouldRefuseAndKeepTheStop()
    {
        using var f = await TwoUnsignedCommitsAsync();
        await FoldAsync(f);
        await MakeKeyAsync(f);
        var stopped = await f.ReadAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new RebaseSkipRequest(stopped), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, RebaseText.FoldNotSkipped), (result.Outcome, result.Message));
        var after = await f.ReadAsync();
        Assert.Equal((RepositoryOperation.Rebase, stopped.Head, stopped.Rebase!.Step), (after.Operation, after.Head, after.Rebase!.Step));
    }

    // A fold Git stopped to amend is skipped as Git skips it: its changes are left out and the rebase goes on.
    [GitFact(2, 56)]
    public async Task WhenAFoldGitStoppedToAmendIsSkipped_ShouldLeaveItsChangesOut()
    {
        using var f = await TwoUnsignedCommitsAsync();
        await FoldAsync(f);
        await MakeKeyAsync(f);

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new RebaseSkipRequest(await f.ReadAsync()), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(("two\none", "a.txt"), (await f.GitAsync("log", "--format=%s"), await f.GitAsync("ls-tree", "--name-only", "HEAD")));
    }

    // A plan the user wrote folds three and four into two with squash; Git cannot commit the first squash, which the key
    // cannot sign, and the key is then made.
    private static async Task<GitFixture> SquashNotSignedAsync()
    {
        var f = await TwoUnsignedCommitsAsync();
        try
        {
            await f.WriteAsync("c.txt", "four\n");
            await f.GitAsync("add", "c.txt");
            await f.GitAsync("-c", "commit.gpgsign=false", "commit", "-m", "four");
            var todo = Path.Combine(f.Container, "todo");
            await File.WriteAllTextAsync(todo, "pick " + await f.GitAsync("rev-parse", "HEAD~2") + "\nsquash " + await f.GitAsync("rev-parse", "HEAD~1")
                + "\nsquash " + await f.GitAsync("rev-parse", "HEAD") + "\n", f.Token);
            await f.Runner.RunAsync(f.Root, ["-c", "sequence.editor=cp '" + todo.Replace('\\', '/') + "'", "rebase", "-i", "HEAD~3"], f.Token);
            await MakeKeyAsync(f);
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private static Task<GitWriteResult> ContinueOrSkipAsync(GitFixture f, RepositorySnapshot stopped, bool continued) =>
        new GitWriteRouter(f.Runner).ExecuteAsync(continued
            ? new RebaseContinueRequest(stopped, MergeMessages.Clean(stopped.Rebase!.Message)) : new RebaseSkipRequest(stopped), f.Token);

    // Continue amends the squash Git stopped at with the message Git prepared for it, and Git folds the rest of the chain
    // after it: each message once.
    [GitFact(2, 56)]
    public async Task WhenASquashGitStoppedToAmendIsContinued_ShouldFoldTheChainWithEachMessageOnce()
    {
        using var f = await SquashNotSignedAsync();
        var stopped = await f.ReadAsync();

        var result = await ContinueOrSkipAsync(f, stopped, continued: true);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(("two\none", "two\n\nthree\n\nfour"), (await f.GitAsync("log", "--format=%s"), await f.GitAsync("log", "-1", "--format=%B")));
        Assert.Matches(SignedCommitTests.Signature, await f.GitAsync("cat-file", "commit", "HEAD"));
    }

    // The last squash of a chain has no fold after it that would give the commit the chain's message, so Continue's amend
    // gives it the message Git prepared, both commits' messages.
    [GitFact(2, 56)]
    public async Task WhenTheLastSquashOfAChainGitStoppedToAmendIsContinued_ShouldKeepEveryMessageOfTheChain()
    {
        using var f = await TwoUnsignedCommitsAsync();
        var todo = Path.Combine(f.Container, "todo");
        await File.WriteAllTextAsync(todo, "pick " + await f.GitAsync("rev-parse", "HEAD~1") + "\nsquash " + await f.GitAsync("rev-parse", "HEAD") + "\n", f.Token);
        await f.Runner.RunAsync(f.Root, ["-c", "core.editor=true", "-c", "sequence.editor=cp '" + todo.Replace('\\', '/') + "'", "rebase", "-i", "HEAD~2"], f.Token);
        await MakeKeyAsync(f);

        var result = await ContinueOrSkipAsync(f, await f.ReadAsync(), continued: true);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(("two\none", "two\n\nthree"), (await f.GitAsync("log", "--format=%s"), await f.GitAsync("log", "-1", "--format=%B")));
        Assert.Matches(SignedCommitTests.Signature, await f.GitAsync("cat-file", "commit", "HEAD"));
    }

    // A squash of a plan the user wrote, put back the same way, gets its message a second time when Git folds it again, as
    // Git 2.43, 2.50 and 2.55 do, on its continue and on its skip alike; Trag refuses both, so the user finishes it in a
    // terminal or aborts.
    [GitBeforeTheory(2, 56)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenASquashGitCouldNotCommitIsContinuedOrSkipped_ShouldRefuseRatherThanRepeatItsMessage(bool continued)
    {
        using var f = await SquashNotSignedAsync();
        var stopped = await f.ReadAsync();
        Assert.True(stopped.Rebase!.FoldNotCommitted);

        var result = await ContinueOrSkipAsync(f, stopped, continued);

        Assert.Equal((GitWriteOutcome.Rejected, RebaseText.FoldRepeatsMessage), (result.Outcome, result.Message));
        var after = await f.ReadAsync();
        Assert.Equal((RepositoryOperation.Rebase, stopped.Head, stopped.Rebase.Step), (after.Operation, after.Head, after.Rebase!.Step));
    }

    // Git's hint at the stop offers git rebase --edit-todo. An exec put above the squash Git put back runs first, and Git
    // then folds the squash again all the same, so Continue still refuses.
    [GitBeforeFact(2, 56)]
    public async Task WhenAnExecIsPutAboveTheSquashGitPutBack_ShouldStillRefuseRatherThanRepeatItsMessage()
    {
        using var f = await SquashNotSignedAsync();
        await f.GitAsync("-c", "sequence.editor=sed -i '1i exec echo ran'", "rebase", "--edit-todo");
        var stopped = await f.ReadAsync();

        var result = await ContinueOrSkipAsync(f, stopped, continued: true);

        Assert.Equal((GitWriteOutcome.Rejected, RebaseText.FoldRepeatsMessage), (result.Outcome, result.Message));
        Assert.Equal((RepositoryOperation.Rebase, stopped.Head), ((await f.ReadAsync()).Operation, (await f.ReadAsync()).Head));
    }

    // A plan changed in a terminal so that it no longer folds the squash Git put back first, dropped or left out, is not the
    // fold Continue makes: Git refuses to go on beside the staged changes, and Trag sends the user back to the terminal.
    [GitBeforeTheory(2, 56)]
    [InlineData("1s/^squash/drop/", true)]
    [InlineData("1s/^squash/drop/", false)]
    [InlineData("1d", true)]
    [InlineData("1d", false)]
    public async Task WhenThePlanNoLongerFoldsTheSquashGitPutBack_ShouldRefuseToContinueOrSkipAndSaySo(string edit, bool continued)
    {
        using var f = await SquashNotSignedAsync();
        await f.GitAsync("-c", "sequence.editor=sed -i " + edit, "rebase", "--edit-todo");
        var stopped = await f.ReadAsync();

        var result = await ContinueOrSkipAsync(f, stopped, continued);

        Assert.Equal((GitWriteOutcome.Rejected, RebaseText.FoldPlanChanged), (result.Outcome, result.Message));
        var after = await f.ReadAsync();
        Assert.Equal((RepositoryOperation.Rebase, stopped.Head, stopped.Rebase!.Step), (after.Operation, after.Head, after.Rebase!.Step));
    }

    // Git folds only what is staged and refuses to go on beside changes that are not, after the fold already moved the
    // branch; Continue refuses first, and the changes stay.
    [Fact]
    public async Task WhenAFoldGitCouldNotCommitIsContinuedWithChangesNotStaged_ShouldRefuseBeforeFolding()
    {
        using var f = await TwoUnsignedCommitsAsync();
        await FoldAsync(f);
        await MakeKeyAsync(f);
        await f.WriteAsync("b.txt", "three, edited\n");
        var stopped = await f.ReadAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new RebaseContinueRequest(stopped, MergeMessages.Clean(stopped.Rebase!.Message)), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, RebaseText.FoldNeedsStaged), (result.Outcome, result.Message));
        Assert.Equal(stopped.Head, (await f.ReadAsync()).Head);
        Assert.Equal("three, edited", (await File.ReadAllTextAsync(Path.Combine(f.Root, "b.txt"), f.Token)).TrimEnd());
    }

    // Git goes on beside a submodule with changes of its own, as one whose build left files in it, so Continue does too.
    [Fact]
    public async Task WhenAFoldGitCouldNotCommitIsContinuedBesideASubmoduleWithChangesOfItsOwn_ShouldFoldIt()
    {
        using var f = await MissingKeyAsync();
        var source = Path.Combine(f.Container, "sub-source");
        await f.GitAtAsync(f.Container, "init", source);
        await f.GitAtAsync(source, "-c", "user.name=Sub", "-c", "user.email=sub@example.invalid", "commit", "--allow-empty", "-m", "Sub");
        await f.GitAsync("-c", "protocol.file.allow=always", "submodule", "add", "--", new Uri(source).AbsoluteUri, "sub");
        await f.GitAsync("-c", "commit.gpgsign=false", "commit", "-m", "two");
        await f.WriteAsync("b.txt", "three\n");
        await f.GitAsync("add", "b.txt");
        await f.GitAsync("-c", "commit.gpgsign=false", "commit", "-m", "three");
        await FoldAsync(f);
        await MakeKeyAsync(f);
        await File.WriteAllTextAsync(Path.Combine(f.Root, "sub", "build.log"), "built\n", f.Token);
        var stopped = await f.ReadAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new RebaseContinueRequest(stopped, MergeMessages.Clean(stopped.Rebase!.Message)), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("two\none", await f.GitAsync("log", "--format=%s"));
    }

    // The fold is folded in with the user's hooks, as every Trag commit is; a hook that refuses it keeps the rebase stopped.
    [Fact]
    public async Task WhenAHookRefusesTheFoldOnContinue_ShouldKeepItStoppedAndSayItDidNotContinue()
    {
        using var f = await TwoUnsignedCommitsAsync();
        await FoldAsync(f);
        await MakeKeyAsync(f);
        var hooks = Path.Combine(f.Container, "refusing-hooks");
        Directory.CreateDirectory(hooks);
        await ShellScript.WriteAsync(Path.Combine(hooks, "pre-commit"), "#!/bin/sh\nexit 1\n", f.Token);
        await f.GitAsync("config", "core.hooksPath", hooks.Replace('\\', '/'));
        var stopped = await f.ReadAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new RebaseContinueRequest(stopped, MergeMessages.Clean(stopped.Rebase!.Message)), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, RebaseText.NotContinued), (result.Outcome, result.Message));
        Assert.Equal((RepositoryOperation.Rebase, stopped.Head), ((await f.ReadAsync()).Operation, (await f.ReadAsync()).Head));
    }

    // Git never signs the commits a stash keeps, so a key that cannot sign does not keep changes from being stashed.
    [Fact]
    public async Task WhenChangesAreStashedWhileTheKeyCannotSign_ShouldStashThemUnsignedAsGitDoes()
    {
        using var f = await MissingKeyAsync();

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new StashRequest(await f.ReadAsync(), new("aside"), StashAction.Push), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("", await f.GitAsync("status", "--porcelain"));
        Assert.DoesNotContain("gpgsig", await f.GitAsync("cat-file", "commit", "refs/stash"));
    }
}
