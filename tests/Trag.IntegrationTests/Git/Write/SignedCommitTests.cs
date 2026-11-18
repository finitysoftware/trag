using System.Diagnostics;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.IntegrationTests.Git.Sync;
using Trag.TestSupport;
using Trag.Domain.Status;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;

namespace Trag.IntegrationTests.Git.Write;

// Commits signed with an SSH key while log.showSignature is on, as a signing user's configuration has it: Git then writes
// its check of the signature before every commit it shows, and Trag still reads the commits it made and the ones it
// rewrites. Every write that makes a commit Git signs has it signed. The key is made with the ssh-keygen on the PATH, which also
// signs.
public sealed class SignedCommitTests
{
    // A SHA-256 repository keeps the signature under gpgsig-sha256.
    internal const string Signature = "(?m)^gpgsig(-sha256)? -----BEGIN SSH SIGNATURE-----";

    private static async Task<GitFixture> SignedAsync()
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            await SignWithNewKeyAsync(f.Root, Path.Combine(f.Container, "signing-key"), f.GitAtAsync, f.Token);
            await f.GitAsync("config", "log.showSignature", "true");
            await f.WriteAsync("a.txt", "one\n");
            await f.CommitAsync("one", "a.txt");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    // Makes a key at the path and has the repository sign its commits with it.
    private static async Task SignWithNewKeyAsync(string root, string key, Func<string, string[], Task<string>> git, CancellationToken ct)
    {
        var keygen = OnPath("ssh-keygen");
        using (var made = Process.Start(new ProcessStartInfo(keygen) { ArgumentList = { "-q", "-t", "ed25519", "-N", "", "-f", key }, UseShellExecute = false })!)
            await made.WaitForExitAsync(ct);
        await git(root, ["config", "gpg.format", "ssh"]);
        await git(root, ["config", "gpg.ssh.program", keygen]);
        await git(root, ["config", "user.signingkey", key + ".pub"]);
        await git(root, ["config", "commit.gpgsign", "true"]);
    }

    // main and feature each made a commit since one.
    private static async Task<(GitFixture F, string Feature)> DivergedAsync()
    {
        var f = await SignedAsync();
        try
        {
            await f.GitAsync("switch", "-c", "feature");
            await f.WriteAsync("b.txt", "feature\n");
            var feature = await f.CommitAsync("feature", "b.txt");
            await f.GitAsync("switch", "main");
            await f.WriteAsync("c.txt", "main\n");
            await f.CommitAsync("main", "c.txt");
            return (f, feature);
        }
        catch { f.Dispose(); throw; }
    }

    private static string OnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .SelectMany(folder => new[] { Path.Combine(folder, program + ".exe"), Path.Combine(folder, program) })
            .First(File.Exists);

    [Fact]
    public async Task WhenTheConfigurationSignsWithAnSshKey_ShouldSayItInTheSnapshot()
    {
        using var signed = await SignedAsync();
        using var unsigned = await GitFixture.CreateAsync("main");
        await unsigned.GitAsync("config", "commit.gpgsign", "false");

        Assert.Equal((CommitSigning.Ssh, CommitSigning.None), ((await signed.ReadAsync()).Signing, (await unsigned.ReadAsync()).Signing));
    }

    // A hand-edited configuration may hold an empty value, false to Git, or a key alone, true to Git.
    [Fact]
    public async Task WhenTheSigningKeysHoldAnEmptyValueOrNone_ShouldReadThemAsGitDoes()
    {
        using var f = await GitFixture.CreateAsync("main");
        await File.AppendAllTextAsync(Path.Combine(f.Root, ".git", "config"), "[commit]\n\tgpgsign =\n[tag]\n\tgpgsign\n", f.Token);

        var snapshot = await f.ReadAsync();

        Assert.Equal((CommitSigning.None, true), (snapshot.Signing, snapshot.SignsTags));
    }

    // Git checks an SSH signature against its allowed signers, and names the signer as that file does.
    [Fact]
    public async Task WhenACommitIsReadWithItsAllowedSigner_ShouldCarryAGoodSignatureFromThatSigner()
    {
        using var f = await SignedAsync();
        var allowed = Path.Combine(f.Container, "allowed-signers");
        var key = (await File.ReadAllTextAsync(Path.Combine(f.Container, "signing-key.pub"), f.Token)).Split(' ');
        await File.WriteAllTextAsync(allowed, "fixture@example.invalid " + key[0] + " " + key[1] + "\n", f.Token);
        await f.GitAsync("config", "gpg.ssh.allowedSignersFile", allowed);

        var details = await new CliHistoryReader(f.Runner).ReadCommitAsync(f.Root, await f.GitAsync("rev-parse", "HEAD"), f.Token);

        Assert.Equal(new CommitSignature(SignatureCheck.Good, "fixture@example.invalid"), details.Signature);
    }

    // Without allowed signers Git cannot check an SSH signature and answers as for an unsigned commit; without the program
    // that checks it answers as for a bad one; a commit changed after it was signed is the bad one.
    [Fact]
    public async Task WhenGitCannotCheckTheSignatureOrItDoesNotMatch_ShouldSayWhichItIs()
    {
        using var f = await SignedAsync();
        var reader = new CliHistoryReader(f.Runner);
        var head = await f.GitAsync("rev-parse", "HEAD");
        var raw = await f.GitAsync("cat-file", "commit", head);
        await f.WriteAsync("changed-commit", raw.Replace("\none", "\nOne") + "\n");
        var changed = await f.GitAsync("hash-object", "-t", "commit", "-w", "changed-commit");

        var noAllowedSigners = (await reader.ReadCommitAsync(f.Root, head, f.Token)).Signature.Check;
        var allowed = Path.Combine(f.Container, "allowed-signers");
        var key = (await File.ReadAllTextAsync(Path.Combine(f.Container, "signing-key.pub"), f.Token)).Split(' ');
        await File.WriteAllTextAsync(allowed, "fixture@example.invalid " + key[0] + " " + key[1] + "\n", f.Token);
        await f.GitAsync("config", "gpg.ssh.allowedSignersFile", allowed);
        var bad = (await reader.ReadCommitAsync(f.Root, changed, f.Token)).Signature.Check;
        await f.GitAsync("config", "gpg.ssh.program", Path.Combine(f.Container, "missing-keygen"));
        var missing = (await reader.ReadCommitAsync(f.Root, head, f.Token)).Signature.Check;

        Assert.Equal((SignatureCheck.NoAllowedSigners, SignatureCheck.Bad, SignatureCheck.CheckerMissing), (noAllowedSigners, bad, missing));
    }

    [Fact]
    public async Task WhenAnUnsignedCommitIsRead_ShouldCarryNoSignature()
    {
        using var f = await SignedAsync();
        await f.GitAsync("-c", "commit.gpgsign=false", "commit", "--allow-empty", "-m", "unsigned");

        var details = await new CliHistoryReader(f.Runner).ReadCommitAsync(f.Root, await f.GitAsync("rev-parse", "HEAD"), f.Token);

        Assert.Equal((CommitSignature.None, "unsigned\n"), (details.Signature, details.Message));
    }

    [Fact]
    public async Task WhenACommitIsMadeAndAmended_ShouldSignBothNewCommits()
    {
        using var f = await SignedAsync();
        var writer = new GitWriteRouter(f.Runner);
        await f.WriteAsync("a.txt", "two\n");
        await f.GitAsync("add", "a.txt");

        var committed = await writer.ExecuteAsync(new CommitRequest(await f.ReadAsync(), "two"), f.Token);
        var two = await f.GitAsync("rev-parse", "HEAD");
        var amended = await writer.ExecuteAsync(new CommitRequest(await f.ReadAsync(), "two, amended", Amend: true), f.Token);

        Assert.True(committed.Outcome == GitWriteOutcome.Succeeded, committed.Message + committed.Details);
        Assert.Equal(two, committed.CommitId);
        Assert.True(amended.Outcome == GitWriteOutcome.Succeeded, amended.Message + amended.Details);
        Assert.Equal("two, amended", await f.GitAsync("log", "-1", "--no-show-signature", "--format=%s"));
        foreach (var commit in new[] { two, "HEAD" })
            Assert.Matches(Signature, await f.GitAsync("cat-file", "commit", commit));
    }

    // Git signs every tag, so each one is an annotated tag: one without a message is refused before Git would ask for it in
    // an editor nobody sees, and one with a message is created signed.
    [Fact]
    public async Task WhenTagsAreSigned_ShouldRefuseATagWithoutAMessageAndSignOneWithAMessage()
    {
        using var f = await SignedAsync();
        await f.GitAsync("config", "tag.gpgSign", "true");
        var head = await f.GitAsync("rev-parse", "HEAD");
        var snapshot = await f.ReadAsync();
        Assert.True(snapshot.SignsTags);
        var writer = new GitWriteRouter(f.Runner);

        var bare = await writer.ExecuteAsync(new TagRequest(snapshot, new("v1", head)), f.Token);
        var annotated = await writer.ExecuteAsync(new TagRequest(snapshot, new("v2", head, "Release")), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, TagOutcomeText.NeedsMessage), (bare.Outcome, bare.Message));
        Assert.Equal("", await f.GitAsync("tag", "--list", "v1"));
        Assert.True(annotated.Outcome == GitWriteOutcome.Succeeded, annotated.Message + annotated.Details);
        Assert.Contains("-----BEGIN SSH SIGNATURE-----", await f.GitAsync("cat-file", "tag", "v2"));
    }

    // tag.forceSignAnnotated has Git sign every annotated tag, which a tag with a message is, as git tag -m does in a terminal.
    [Fact]
    public async Task WhenAnnotatedTagsAreForcedToBeSigned_ShouldSignATagWithAMessage()
    {
        using var f = await SignedAsync();
        await f.GitAsync("config", "tag.forceSignAnnotated", "true");
        var head = await f.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new TagRequest(await f.ReadAsync(), new("v1", head, "Release")), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Contains("-----BEGIN SSH SIGNATURE-----", await f.GitAsync("cat-file", "tag", "v1"));
    }

    // Git appends the signature right after the message, and looks for it only at the start of a line: after a message
    // whose last line has no line break, git verify-tag does not find it and the tag's subject runs on into it.
    [Theory]
    [InlineData("Release", "Release\n")]
    [InlineData("Release\n", "Release\n")]
    public async Task WhenASignedTagIsGivenAMessage_ShouldEndItsLastLineSoGitFindsTheSignature(string typed, string kept)
    {
        using var f = await SignedAsync();
        await f.GitAsync("config", "tag.gpgSign", "true");
        var head = await f.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new TagRequest(await f.ReadAsync(), new("v1", head, typed)), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Contains("\n\n" + kept + "-----BEGIN SSH SIGNATURE-----\n", await f.GitAsync("cat-file", "tag", "v1"));
    }

    [Fact]
    public async Task WhenTheAuthorsOfSignedCommitsAreRead_ShouldReadThemAsUsual()
    {
        using var f = await SignedAsync();

        var authors = await new CliHistoryReader(f.Runner).ReadAuthorsAsync(f.Root, f.Token);

        Assert.Equal([(await f.ReadAsync()).Author!], authors);
    }

    [Fact]
    public async Task WhenSignedCommitsAreRewritten_ShouldListThemAsGitHasThem()
    {
        using var f = await SignedAsync();
        await f.WriteAsync("a.txt", "two\n");
        var two = await f.CommitAsync("two", "a.txt");

        var range = await new CliBranchReader(f.Runner).ReadRewriteAsync(f.Root, two, f.Token);

        Assert.Equal([(two, "two")], range!.Steps.Select(step => (step.Id, step.Subject)));
    }

    // A rewrite replays every commit after its base, and the reworded one too: each new commit carries a signature made
    // with the configured key.
    [Fact]
    public async Task WhenSignedCommitsAreRewritten_ShouldSignTheNewCommitsWithTheConfiguredKey()
    {
        using var f = await SignedAsync();
        var start = await f.GitAsync("rev-parse", "HEAD");
        await f.WriteAsync("b.txt", "two\n"); var two = await f.CommitAsync("two", "b.txt");
        await f.WriteAsync("c.txt", "three\n"); var three = await f.CommitAsync("three", "c.txt");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new RewriteRequest(await f.ReadAsync(), start,
            [new(two, "two", "two", Trag.Domain.Rebase.RewriteAction.Reword, "two, reworded"), new(three, "three", "three", Trag.Domain.Rebase.RewriteAction.Pick, "three")]), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        foreach (var commit in new[] { "HEAD", "HEAD~1" })
            Assert.Matches(Signature, await f.GitAsync("cat-file", "commit", commit));
        Assert.NotEqual(two, await f.GitAsync("rev-parse", "HEAD~1"));
    }

    [Fact]
    public async Task WhenABranchIsMergedIntoASignedOne_ShouldSignTheMergeCommit()
    {
        var (f, feature) = await DivergedAsync();
        using var _ = f;

        var merged = await new GitWriteRouter(f.Runner).ExecuteAsync(
            new MergeRequest(await f.ReadAsync(), "feature", feature, false, "Merge branch 'feature'"), f.Token);

        Assert.True(merged.Outcome == GitWriteOutcome.Succeeded, merged.Message + merged.Details);
        Assert.Matches(Signature, await f.GitAsync("cat-file", "commit", "HEAD"));
    }

    // The key lives in the container, outside both clones.
    [Fact]
    public async Task WhenAPullMergesADivergedBranch_ShouldSignTheMergeCommit()
    {
        using var f = await SyncFixture.Create();
        await MergeScenario.DivergeCleanly(f);
        var parents = await f.Head(f.One) + " " + await f.Head(f.Two);
        await SignWithNewKeyAsync(f.One, Path.Combine(f.Root, "signing-key"), f.Git, f.Token);

        var pulled = await f.Execute(SyncAction.Pull);

        Assert.True(pulled.Outcome == GitWriteOutcome.Succeeded, pulled.Message + pulled.Details);
        Assert.Equal(parents, await f.Git(f.One, "show", "-s", "--format=%P", "HEAD"));
        Assert.Matches(Signature, await f.Git(f.One, "cat-file", "commit", "HEAD"));
    }

    [Fact]
    public async Task WhenACommitIsCherryPicked_ShouldSignTheNewCommit()
    {
        var (f, feature) = await DivergedAsync();
        using var _ = f;

        var picked = await new GitWriteRouter(f.Runner).ExecuteAsync(new CherryPickRequest(await f.ReadAsync(), feature), f.Token);

        Assert.True(picked.Outcome == GitWriteOutcome.Succeeded, picked.Message + picked.Details);
        Assert.Equal("feature", await f.GitAsync("log", "-1", "--no-show-signature", "--format=%s"));
        Assert.Matches(Signature, await f.GitAsync("cat-file", "commit", "HEAD"));
    }

    [Fact]
    public async Task WhenACommitIsReverted_ShouldSignTheRevertCommit()
    {
        var (f, _) = await DivergedAsync();
        using var __ = f;
        var head = await f.GitAsync("rev-parse", "HEAD");

        var reverted = await new GitWriteRouter(f.Runner).ExecuteAsync(new RevertRequest(await f.ReadAsync(), head), f.Token);

        Assert.True(reverted.Outcome == GitWriteOutcome.Succeeded, reverted.Message + reverted.Details);
        Assert.Equal(head, await f.GitAsync("rev-parse", "HEAD~1"));
        Assert.Matches(Signature, await f.GitAsync("cat-file", "commit", "HEAD"));
    }

    [Fact]
    public async Task WhenABranchIsRebased_ShouldSignEveryReplayedCommit()
    {
        var (f, _) = await DivergedAsync();
        using var __ = f;
        var main = await f.GitAsync("rev-parse", "HEAD");
        await f.GitAsync("switch", "feature");
        await f.WriteAsync("d.txt", "feature\n");
        await f.CommitAsync("feature, more", "d.txt");

        var rebased = await new GitWriteRouter(f.Runner).ExecuteAsync(new RebaseRequest(await f.ReadAsync(), "main", main), f.Token);

        Assert.True(rebased.Outcome == GitWriteOutcome.Succeeded, rebased.Message + rebased.Details);
        Assert.Equal(main, await f.GitAsync("rev-parse", "HEAD~2"));
        foreach (var commit in new[] { "HEAD", "HEAD~1" })
            Assert.Matches(Signature, await f.GitAsync("cat-file", "commit", commit));
    }
}
