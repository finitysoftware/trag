using Trag.Infrastructure.Git.Processes;

namespace Trag.IntegrationTests;

// The runner against real git processes: an output limit that stops the process, a request cancelled before it starts,
// a cancellation that reaches the shell child, and both streams drained together.
public sealed class GitProcessControlTests
{
    [Fact]
    public async Task WhenTheOutputExceedsTheLimit_ShouldStopGitWithoutHanging()
    {
        var runner = new GitProcessRunner(4);
        await Assert.ThrowsAsync<GitOutputLimitException>(() => runner.RunAsync(Path.GetTempPath(), ["--version"], Timeout()));
    }

    [Fact]
    public async Task WhenTheRequestIsAlreadyCancelled_ShouldHonorTheCancellation()
    {
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitProcessRunner().RunAsync(Path.GetTempPath(), ["--version"], ct.Token));
    }

    [Fact]
    public async Task WhenRunningGitIsCancelled_ShouldStopItAndItsShellChild()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitProcessRunner().RunAsync(Path.GetTempPath(),
            ["-c", "alias.tragwait=!sleep 30", "tragwait"], cancellation.Token));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5));
    }

    // A job a hook starts in the background holds Git's streams after Git is done; the run ends with Git, as it does in a
    // terminal, and keeps what Git wrote.
    [Fact]
    public async Task WhenAChildOfGitOutlivesItHoldingTheStreams_ShouldEndWithGit()
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var result = await new GitProcessRunner().RunAsync(Path.GetTempPath(), ["-c", "alias.tragjob=!sleep 30 & echo started", "tragjob"], Timeout());
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), timer.Elapsed.ToString());
        Assert.Equal("started\n", System.Text.Encoding.UTF8.GetString(result.StandardOutput));
    }

    [Fact]
    public async Task WhenGitWritesBothStreamsAtLength_ShouldDrainThemTogether()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await new GitProcessRunner().RunAsync(Path.GetTempPath(),
            ["-c", "alias.tragstream=!i=0; while [ $i -lt 5000 ]; do echo output; echo error >&2; i=$((i+1)); done", "tragstream"], timeout.Token);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.StandardOutput.Length >= 35_000);
        Assert.True(result.StandardError.Length >= 30_000);
    }

    private static CancellationToken Timeout() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;
}
