using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.UnitTests.Git.State;

// The shape every Git write shares. Ten operations rely on these four promises, so they are asserted here once rather
// than inferred from ten integration tests.
public sealed class VerifiedGitOperationTests
{
    private static readonly GitResult Done = new(0, [], []);
    private static readonly TimeSpan Verification = TimeSpan.FromSeconds(5);

    private static GitWriteResult Say(string message) => new(GitWriteOutcome.Succeeded, message);

    [Fact]
    public async Task WhenTheCommandFinishes_ShouldReachTheVerificationWithNothingToExplain()
    {
        var result = await VerifiedGitOperation.RunAsync(
            _ => Task.FromResult(Done),
            (command, failure, _) => Task.FromResult(Say(command == Done && failure is null ? "as run" : "unexpected")),
            (_, _) => Say("recovered"), TimeSpan.FromMinutes(1), Verification, CancellationToken.None);

        Assert.Equal("as run", result.Message);
    }

    // Git may already have done the work, so the verification decides - it is never skipped because the run threw.
    [Fact]
    public async Task WhenTheCommandThrows_ShouldHandWhateverStoppedItToTheVerificationRatherThanThrow()
    {
        var result = await VerifiedGitOperation.RunAsync(
            _ => throw new InvalidOperationException("stopped"),
            (command, failure, _) => Task.FromResult(Say(command is null && failure is InvalidOperationException { Message: "stopped" } ? "asked anyway" : "unexpected")),
            (_, _) => Say("recovered"), TimeSpan.FromMinutes(1), Verification, CancellationToken.None);

        Assert.Equal("asked anyway", result.Message);
    }

    [Fact]
    public async Task WhenTheCommandRunsOverItsOwnLimit_ShouldTimeOutWithoutCancellingTheCaller()
    {
        using var caller = new CancellationTokenSource();

        var limited = await RunOverTheLimitAsync(caller.Token);
        Assert.Equal("timed out", limited.Message);
        Assert.False(caller.IsCancellationRequested);
    }

    // A Git its own limit stopped is killed as one Cancel stops, and the index lock it held goes as it does then.
    [Fact]
    public async Task WhenTheCommandHoldingTheIndexLockRunsOverItsOwnLimit_ShouldRemoveTheLockItLeft()
    {
        var gitDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "trag-lock-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var lockFile = Path.Combine(gitDirectory, "index.lock");

            await VerifiedGitOperation.RunAsync(
                async token => { await File.WriteAllBytesAsync(lockFile, [], token); await Task.Delay(Timeout.Infinite, token); return Done; },
                (_, _, _) => Task.FromResult(Say("verified")), (_, _) => Say("recovered"),
                TimeSpan.FromMilliseconds(50), Verification, CancellationToken.None, new StoppedWriteLock(Path.Combine(gitDirectory, "index")));

            Assert.False(File.Exists(lockFile));
        }
        finally { Directory.Delete(gitDirectory, true); }
    }

    // A merge that may wait for a signing key or write Git LFS content has no limit of its own; Cancel still stops it.
    [Fact]
    public async Task WhenACommandWithoutALimitIsCanceledWhileHoldingALock_ShouldRemoveTheLockItLeft()
    {
        var gitDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "trag-lock-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var lockFile = Path.Combine(gitDirectory, "HEAD.lock");
            using var caller = new CancellationTokenSource();

            await VerifiedGitOperation.RunAsync(
                async token => { await File.WriteAllBytesAsync(lockFile, [], token); await caller.CancelAsync(); await Task.Delay(Timeout.Infinite, token); return Done; },
                (_, _, _) => Task.FromResult(Say("verified")), (_, _) => Say("recovered"),
                operationTimeout: null, Verification, caller.Token, new StoppedWriteLock(Path.Combine(gitDirectory, "HEAD")));

            Assert.False(File.Exists(lockFile));
        }
        finally { Directory.Delete(gitDirectory, true); }
    }

    [Fact]
    public async Task WhenTheCommandHasNoLimit_ShouldRunOnTheCallersToken()
    {
        using var caller = new CancellationTokenSource();
        await RunOverTheLimitAsync(caller.Token);

        var unlimited = await VerifiedGitOperation.RunAsync(
            token => Task.FromResult(token == caller.Token ? Done : throw new InvalidOperationException("another token")),
            (_, failure, _) => Task.FromResult(Say(failure is null ? "the caller's own" : "unexpected")),
            (_, _) => Say("recovered"), operationTimeout: null, Verification, caller.Token);
        Assert.Equal("the caller's own", unlimited.Message);
    }

    [Fact]
    public async Task WhenTheRequestIsAlreadyCancelled_ShouldStillVerifyOnATokenOfItsOwn()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var result = await VerifiedGitOperation.RunAsync(
            _ => Task.FromResult(Done),
            (_, _, token) => Task.FromResult(Say(token.IsCancellationRequested ? "unexpected" : "verified after cancellation")),
            (_, _) => Say("recovered"), TimeSpan.FromMinutes(1), Verification, cancelled.Token);

        Assert.Equal("verified after cancellation", result.Message);
    }

    [Fact]
    public async Task WhenTheVerificationCannotFinish_ShouldRecoverWithTheCommandAndTheError()
    {
        var result = await VerifiedGitOperation.RunAsync(
            _ => Task.FromResult(Done),
            (_, _, _) => throw new IOException("unreadable"),
            (command, error) => Say(command == Done && error is IOException { Message: "unreadable" } ? "recovered with the command" : "unexpected"),
            TimeSpan.FromMinutes(1), Verification, CancellationToken.None);

        Assert.Equal("recovered with the command", result.Message);
    }

    [Fact]
    public async Task WhenTheVerificationHangs_ShouldRunOutOfTimeAndRecoverRatherThanHang()
    {
        var result = await VerifiedGitOperation.RunAsync(
            _ => Task.FromResult(Done),
            async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); return Say("unexpected"); },
            (_, error) => Say(error is OperationCanceledException ? "verification timed out" : "unexpected"),
            TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.Equal("verification timed out", result.Message);
    }

    // A command that never finishes on its own, stopped by its 50 ms limit.
    private static Task<GitWriteResult> RunOverTheLimitAsync(CancellationToken caller) => VerifiedGitOperation.RunAsync(
        async token => { await Task.Delay(Timeout.Infinite, token); return Done; },
        (_, failure, _) => Task.FromResult(Say(failure is OperationCanceledException ? "timed out" : "unexpected")),
        (_, _) => Say("recovered"), TimeSpan.FromMilliseconds(50), Verification, caller);
}
