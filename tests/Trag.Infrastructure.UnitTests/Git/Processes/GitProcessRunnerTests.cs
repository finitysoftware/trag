using System.Diagnostics;
using System.Text;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Setup;

namespace Trag.Infrastructure.UnitTests.Git.Processes;

// Drives the runner with git's own "!shell" aliases so no repository is needed.
public sealed class GitProcessRunnerTests
{
    private static readonly string Root = Path.GetTempPath();
    private static CancellationToken Timeout(int seconds = 20) => new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;
    private static string[] Alias(string script) => ["-c", "alias.tragtest=!" + script, "tragtest"];
    // The shell of Git for Windows has its own process ids and gives the Windows one in /proc; elsewhere the shell's is the process's.
    private static string ProcessId(string shellId) => OperatingSystem.IsWindows() ? $"$(cat /proc/{shellId}/winpid)" : shellId;
    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).Trim();

    private static async Task<T> WithEnvironment<T>(IReadOnlyDictionary<string, string?> variables, Func<Task<T>> action)
    {
        var previous = variables.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        foreach (var (key, value) in variables) Environment.SetEnvironmentVariable(key, value);
        try { return await action(); }
        finally { foreach (var (key, value) in previous) Environment.SetEnvironmentVariable(key, value); }
    }

    // With a trace variable set, a plain run and then an askpass run on the same runner, each echoing the variable.
    private static Task<(string Plain, string Askpass)> TraceThroughBothRunsAsync()
    {
        var trace = new Dictionary<string, string?> { ["GIT_TRACE_TRAGTEST"] = "1" };
        var runner = new GitProcessRunner();
        return WithEnvironment(trace, async () =>
        {
            var plain = await runner.RunAsync(Root, Alias("echo \"${GIT_TRACE_TRAGTEST:-unset}\""), Timeout());
            var askpass = await runner.RunAsync(Root, Alias("echo \"${GIT_TRACE_TRAGTEST:-unset}\""), Timeout(), new Dictionary<string, string?> { ["TRAG_ASKPASS"] = "1" });
            return (Text(plain.StandardOutput), Text(askpass.StandardOutput));
        });
    }

    // A runner that has delivered standard input to one command.
    private static async Task<GitProcessRunner> AfterStandardInputAsync()
    {
        var runner = new GitProcessRunner();
        await runner.RunAsync(Root, Alias("cat"), Timeout(), standardInput: "hello from stdin\n"u8.ToArray());
        return runner;
    }

    // ...and then closed it for a command given none.
    private static async Task<GitProcessRunner> AfterClosedInputAsync()
    {
        var runner = await AfterStandardInputAsync();
        await runner.RunAsync(Root, Alias("cat; echo done"), Timeout());
        return runner;
    }

    // Reads the first word of a command that would go on and fail, and stops there.
    private static Task<string> ReadTheFirstWordAsync(GitProcessRunner runner) =>
        runner.ReadStreamAsync(Root, Alias("echo first; echo second; exit 4"), async (stream, ct) =>
        {
            var buffer = new byte[5];
            await stream.ReadExactlyAsync(buffer, ct);
            return (Encoding.UTF8.GetString(buffer), false);
        }, Timeout());

    [Fact]
    public async Task WhenTheLaunchingTerminalSetsRepositoryRedirections_ShouldKeepThemFromGitAndStillDisablePrompts()
    {
        var poison = new Dictionary<string, string?> { ["GIT_DIR"] = "poison", ["GIT_WORK_TREE"] = "poison", ["GIT_INDEX_FILE"] = "poison", ["GIT_NAMESPACE"] = "poison", ["GIT_COMMON_DIR"] = "poison" };
        var result = await WithEnvironment(poison, () => new GitProcessRunner().RunAsync(Root,
            Alias("echo \"${GIT_DIR:-unset} ${GIT_WORK_TREE:-unset} ${GIT_INDEX_FILE:-unset} ${GIT_NAMESPACE:-unset} ${GIT_COMMON_DIR:-unset} $GIT_TERMINAL_PROMPT $GIT_OPTIONAL_LOCKS $LC_ALL\""), Timeout()));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("unset unset unset unset unset 0 0 C", Text(result.StandardOutput));
    }

    [Fact]
    public async Task WhenATraceVariableIsSetForAPlainRun_ShouldPassItToGit()
    {
        var (plain, _) = await TraceThroughBothRunsAsync();
        Assert.Equal("1", plain);
    }

    [Fact]
    public async Task WhenATraceVariableIsSetForAnAskpassRun_ShouldRemoveIt()
    {
        var (_, askpass) = await TraceThroughBothRunsAsync();
        Assert.Equal("unset", askpass);
    }

    // One echoed line shows all three: the override, the removal and the addition.
    [Fact]
    public async Task WhenTheCallerGivesItsOwnEntries_ShouldOverrideTheDefaultsAndLetNullRemoveAVariable()
    {
        var result = await WithEnvironment(new Dictionary<string, string?> { ["TRAG_TEST_REMOVE"] = "present" }, () => new GitProcessRunner().RunAsync(Root,
            Alias("echo \"$LC_ALL ${TRAG_TEST_REMOVE:-unset} $TRAG_TEST_ADD\""), Timeout(),
            new Dictionary<string, string?> { ["LC_ALL"] = "C.UTF-8", ["TRAG_TEST_REMOVE"] = null, ["TRAG_TEST_ADD"] = "added" }));
        Assert.Equal("C.UTF-8 unset added", Text(result.StandardOutput));
    }

    [Fact]
    public async Task WhenTheOutputPassesThePrefixLimit_ShouldKeepOnlyThePrefixAndStopTheProcess()
    {
        var timer = Stopwatch.StartNew();
        var result = await new GitProcessRunner().RunAsync(Root,
            Alias("i=0; while [ $i -lt 4000 ]; do echo 0123456789abcdef; i=$((i+1)); done; sleep 20"), Timeout(30), standardOutputPrefixLimit: 1024);
        Assert.True(result.OutputTruncated);
        Assert.True(result.StandardOutput.Length <= 1024, result.StandardOutput.Length.ToString());
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), timer.Elapsed.ToString());
    }

    // A transfer runs as long as Git keeps saying something: every line on either stream starts the quiet limit again, so
    // a run longer than the limit ends as Git ends it.
    [Fact]
    public async Task WhenGitKeepsTalkingLongerThanTheQuietLimit_ShouldLetItFinish()
    {
        var result = await new GitProcessRunner().RunAsync(Root, Alias("for i in 1 2 3 4 5 6; do echo step $i >&2; sleep 0.4; done; echo done"), Timeout(),
            quietLimit: TimeSpan.FromSeconds(1.5));

        Assert.Equal((0, "done"), (result.ExitCode, Text(result.StandardOutput)));
    }

    // Git that says nothing for the quiet limit is stopped, as a cancellation the caller did not ask for.
    [Fact]
    public async Task WhenGitSaysNothingForTheQuietLimit_ShouldStopIt()
    {
        var caller = Timeout();
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitProcessRunner().RunAsync(Root, Alias("echo start >&2; sleep 10"), caller,
            quietLimit: TimeSpan.FromSeconds(1)));

        Assert.False(caller.IsCancellationRequested);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), watch.Elapsed.ToString());
    }

    // Cancel stops the processes Git started as well as Git, as the shell of an alias or a hook: stopping Git alone would
    // leave the shell running after the run ended.
    [Fact]
    public async Task WhenGitRunsAShell_ShouldStopTheShellTooOnCancel()
    {
        var marker = Path.Combine(Root, "trag-tree-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        var run = new GitProcessRunner().RunAsync(Root, Alias($"echo {ProcessId("$$")} > '{marker.Replace('\\', '/')}'; sleep 30"), cancellation.Token);
        using var shell = Process.GetProcessById(await WrittenLineAsync(marker));
        File.Delete(marker);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(shell.WaitForExit(5000), "The shell outlived the cancelled run.");
    }

    // A command the shell starts runs in a process whose Windows parent is gone, as the shell forks and then execs it: the
    // tree no longer reaches it, and Cancel still stops it.
    [Fact]
    public async Task WhenTheShellGitRunsStartsACommand_ShouldStopTheCommandTooOnCancel()
    {
        var marker = Path.Combine(Root, "trag-tree-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        var run = new GitProcessRunner().RunAsync(Root, Alias($"sleep 30 & sleep 1; echo {ProcessId("$!")} > '{marker.Replace('\\', '/')}'; wait"), cancellation.Token);
        using var command = Process.GetProcessById(await WrittenLineAsync(marker));
        File.Delete(marker);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(command.WaitForExit(5000), "The command outlived the cancelled run.");
    }

    // A cancellation that arrives once Git has ended by itself stops nothing, so the run gives what Git did.
    [Fact]
    public async Task WhenCancelArrivesAfterGitExitedWithZero_ShouldReturnTheResult()
    {
        using var cancellation = new CancellationTokenSource();

        var result = await new GitProcessRunner().RunAsync(Root, Alias($"echo done; echo \"Receiving objects: {ProcessId("$$")}\" >&2"), cancellation.Token,
            standardErrorProgress: new CancelOnceGitHasExited(cancellation));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal((0, "done"), (result.ExitCode, Text(result.StandardOutput)));
    }

    [Fact]
    public async Task WhenStandardInputIsGiven_ShouldDeliverItToGit()
    {
        var runner = new GitProcessRunner();
        var echoed = await runner.RunAsync(Root, Alias("cat"), Timeout(), standardInput: "hello from stdin\n"u8.ToArray());
        Assert.Equal(0, echoed.ExitCode); Assert.Equal("hello from stdin", Text(echoed.StandardOutput));
    }

    [Fact]
    public async Task WhenNoStandardInputIsGiven_ShouldCloseIt()
    {
        var runner = await AfterStandardInputAsync();
        var closed = await runner.RunAsync(Root, Alias("cat; echo done"), Timeout());
        Assert.Equal("done", Text(closed.StandardOutput));
    }

    [Fact]
    public async Task WhenGitExitsWithAnError_ShouldReportTheExitCodeAndErrorInsteadOfThrowing()
    {
        var runner = await AfterClosedInputAsync();
        var failed = await runner.RunAsync(Root, Alias("echo err >&2; exit 3"), Timeout());
        Assert.Equal(3, failed.ExitCode); Assert.Equal("err", Text(failed.StandardError)); Assert.False(failed.OutputTruncated);
    }

    [Fact]
    public async Task WhenAStreamingReadStopsEarly_ShouldReturnWithoutAnError()
    {
        var runner = new GitProcessRunner();
        var early = await ReadTheFirstWordAsync(runner);
        Assert.Equal("first", early);
    }

    [Fact]
    public async Task WhenAStreamingReadGoesToTheEndOfAFailedCommand_ShouldReportTheExitCode()
    {
        var runner = new GitProcessRunner();
        await ReadTheFirstWordAsync(runner);
        var error = await Assert.ThrowsAsync<GitCommandException>(() => runner.ReadStreamAsync(Root, Alias("echo first; exit 4"), async (stream, ct) =>
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return (Encoding.UTF8.GetString(buffer.ToArray()), true);
        }, Timeout()));
        Assert.Equal(4, error.ExitCode);
    }

    // A job Git leaves running holds both of Git's streams after Git is done, as a filter's or hook's background job does;
    // a streaming read ends with Git too, as a run does, and the reader gets what Git wrote.
    [Fact]
    public async Task WhenAChildOfGitOutlivesItHoldingTheStreamsDuringAStreamingRead_ShouldEndWithGit()
    {
        var timer = Stopwatch.StartNew();

        var read = await new GitProcessRunner().ReadStreamAsync(Root, Alias("sleep 30 & echo started"), async (stream, ct) =>
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return (Encoding.UTF8.GetString(buffer.ToArray()), true);
        }, Timeout(60));

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), timer.Elapsed.ToString());
        Assert.Equal("started\n", read);
    }

    [Fact]
    public async Task WhenStandardErrorPassesTheLimitWhileStreaming_ShouldFail()
    {
        var runner = new GitProcessRunner(outputLimit: 16);
        await Assert.ThrowsAsync<GitOutputLimitException>(() => runner.ReadStreamAsync(Root, Alias("echo aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa >&2; sleep 1"), async (stream, ct) =>
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return (buffer.Length, true);
        }, Timeout()));
    }

    // A Git that fills its error stream past the limit is stopped there, not waited for until it ends by itself.
    [Fact]
    public async Task WhenStandardErrorExceedsTheLimitInRunAsync_ShouldThrowAndStopGit()
    {
        var timer = Stopwatch.StartNew();
        var runner = new GitProcessRunner(outputLimit: 16);

        await Assert.ThrowsAsync<GitOutputLimitException>(() => runner.RunAsync(Root,
            Alias("echo aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa >&2; sleep 20"), Timeout(30)));

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), timer.Elapsed.ToString());
    }

    [Fact]
    public async Task WhenARunAndAStreamingReadFinish_ShouldReportEachExit()
    {
        var exits = new List<int>();
        var runner = new GitProcessRunner(exited: exits.Add);
        await runner.RunAsync(Root, ["--version"], Timeout());
        await runner.ReadStreamAsync(Root, ["--version"], async (stream, ct) => { using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer, ct); return (buffer.Length, true); }, Timeout());
        Assert.Equal(2, exits.Count); Assert.All(exits, id => Assert.True(id > 0));
    }

    // Every run starts the Git the shared executable names at that moment, so a Git found or chosen later is the one used.
    [Fact]
    public async Task WhenTheSharedExecutableChanges_ShouldStartTheGitItNamesFromThenOn()
    {
        var executable = new GitExecutable { Path = Path.Combine(Root, "no-git-here", GitSearch.FileName) };
        var runner = new GitProcessRunner(executable: executable);
        await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(() => runner.RunAsync(Root, ["--version"], Timeout()));

        executable.Path = "git";

        Assert.Equal(0, (await runner.RunAsync(Root, ["--version"], Timeout())).ExitCode);
    }

    // The number a shell writes as one whole line into the file, once it is there.
    private static async Task<int> WrittenLineAsync(string path)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var text = File.ReadAllText(path);
                if (text.EndsWith('\n')) return int.Parse(text);
            }
            catch (IOException) { }
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "The shell never wrote the line.");
            await Task.Delay(50);
        }
    }

    // Takes the Windows process id of the alias's shell from Git's last line, waits until that shell has exited and then
    // Git after it, and cancels while the runner still reads the line.
    private sealed class CancelOnceGitHasExited(CancellationTokenSource cancellation) : IProgress<string>
    {
        public void Report(string value)
        {
            try { using var shell = Process.GetProcessById(int.Parse(value[(value.LastIndexOf(' ') + 1)..])); shell.WaitForExit(); }
            catch (ArgumentException) { }
            // Git ends right after its alias's shell, and nothing outside it says when.
            Thread.Sleep(1000);
            cancellation.Cancel();
        }
    }
}
