using System.Text;
using static Trag.IntegrationTests.Git.Sync.SyncFixture;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Sync;

namespace Trag.IntegrationTests.Git.Sync;

// A transfer that does not finish on its own: cancelled or timed out, before or after the server or the working tree
// changed, and one that must not wait for a prompt at all. The verification, not the interruption, decides the outcome.
public sealed class SyncInterruptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAPushIsCancelled_ShouldCheckWhetherTheServerAlreadyAcceptedTheCommit(bool afterAcceptance)
    {
        using var f = await SyncFixture.Create(); var marker = Path.Combine(f.Root, "push-started");
        await f.Hook(f.Bare, afterAcceptance ? "post-receive" : "pre-receive", "sleep 30 & echo ready > " + Quote(marker) + "\nwait\n");
        var before = await f.Head(f.Bare); await f.Commit(f.One, "cancelled push");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var pending = f.Execute(SyncAction.Push, token: cancel.Token);
        await WaitFor(marker, f.Token); cancel.Cancel(); var result = await pending;
        Assert.Equal(afterAcceptance ? GitWriteOutcome.Succeeded : GitWriteOutcome.Cancelled, result.Outcome);
        Assert.Equal(afterAcceptance ? await f.Head(f.One) : before, await f.Head(f.Bare));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAFetchIsCancelledOrTimesOut_ShouldReportItCancelled(bool timeout)
    {
        using var f = await SyncFixture.Create(); var marker = Path.Combine(f.Root, "fetch-started"); var script = Path.Combine(f.Root, "slow-upload.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nsleep 30 & echo ready > " + Quote(marker) + "\nwait\nexec git-upload-pack \"$@\"\n", new UTF8Encoding(false), f.Token);
        await f.Git(f.One, "config", "remote.origin.uploadpack", "sh " + Quote(script));
        var sync = timeout ? new GitSyncFacade(new(), f.Reader, TimeSpan.FromMilliseconds(800)) : f.Sync;
        var expected = await f.Reader.ReadAsync(f.One, f.Token); var state = await sync.ReadAsync(expected, "origin", null, f.Token);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var pending = sync.ExecuteAsync(new SyncRequest(expected, state, SyncAction.Fetch), cancel.Token);
        await WaitFor(marker, f.Token); if (!timeout) cancel.Cancel();
        var result = await pending; Assert.Equal(GitWriteOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task WhenSshAuthenticationIsMissing_ShouldFailWithoutPromptingOrReachingTheNetwork()
    {
        using var f = await SyncFixture.Create(); var script = Path.Combine(f.Root, "fake-ssh.sh"); var marker = Path.Combine(f.Root, "ssh-options");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nprintf '%s\\n' \"$@\" \"$GIT_TERMINAL_PROMPT\" \"$GCM_INTERACTIVE\" > " + Quote(marker)
            + "\necho 'Permission denied (publickey).' >&2\nexit 255\n", new UTF8Encoding(false), f.Token);
        await f.Git(f.One, "config", "core.sshCommand", "sh " + Quote(script)); await f.Git(f.One, "config", "ssh.variant", "ssh");
        await f.Git(f.One, "remote", "set-url", "origin", "ssh://fixture@example.invalid/repository.git");
        var result = await f.Execute(SyncAction.Fetch);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome); Assert.Contains("authentication", result.Message);
        var args = await File.ReadAllTextAsync(marker, f.Token); Assert.Contains("BatchMode=yes", args); Assert.Contains("Never", args);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAFastForwardIsInterrupted_ShouldVerifyTheRealHeadAndKeepAnUncertainIndex(bool changeHead)
    {
        using var f = await SyncFixture.Create(); var before = await f.Head(f.One);
        await f.Commit(f.Two, "incoming pull"); await f.Git(f.Two, "push", "origin", "trunk");
        var target = await f.Head(f.Two); var marker = Path.Combine(f.Root, "pull-started");
        await f.Hook(f.One, "post-merge", (changeHead ? "git update-ref refs/heads/trunk " + before + "\n" : "")
            + "sleep 30 & echo ready > " + Quote(marker) + "\nwait\n");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var pending = f.Execute(SyncAction.Pull, token: cancel.Token);
        await WaitFor(marker, f.Token); cancel.Cancel(); var result = await pending;
        Assert.True(result.Outcome == (changeHead ? GitWriteOutcome.Uncertain : GitWriteOutcome.Succeeded), result.Message + result.Details);
        Assert.Equal(changeHead ? before : target, await f.Head(f.One));
        Assert.False(File.Exists(Path.Combine(f.One, ".git", "index.lock")));
        if (changeHead) Assert.Single(Directory.EnumerateFiles(Path.Combine(f.One, ".git"), "trag-write-*.index"));
        else Assert.Empty((await f.Reader.ReadAsync(f.One, f.Token)).Files);
    }
}
