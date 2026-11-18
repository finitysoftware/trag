namespace Trag.Infrastructure.Git.Processes;

// The one entry point to Git for the rest of the layer: it builds the command line, starts Git, and either drains both
// streams into memory or hands stdout to the caller as it arrives.
public sealed class GitProcessRunner(int outputLimit = 8 * 1024 * 1024, Action<int>? exited = null, GitExecutable? executable = null)
{
    private readonly GitCommandLine commandLine = new(executable ?? new());

    // The Git program the next run starts.
    public string Program => commandLine.Program;

    // Both streams drained into memory, bounded by the caller's prefix limit and the runner's output limit. With a quiet
    // limit Git runs as long as it keeps saying something: every chunk on either stream starts the limit again, and a
    // limit that runs out stops Git as a cancellation the caller did not ask for. A cancellation that comes after Git ended
    // by itself stopped nothing, so the run gives what Git did.
    public async Task<GitResult> RunAsync(string repository, IEnumerable<string> arguments,
        CancellationToken ct, IReadOnlyDictionary<string, string?>? environment = null, byte[]? standardInput = null,
        int? standardOutputPrefixLimit = null, IProgress<string>? standardErrorProgress = null, bool literalPathspecs = true, TimeSpan? quietLimit = null)
    {
        using var quiet = quietLimit is null ? null : CancellationTokenSource.CreateLinkedTokenSource(ct);
        Action? heard = quiet is null ? null : () => quiet.CancelAfter(quietLimit!.Value);
        heard?.Invoke();
        var token = quiet?.Token ?? ct;
        await using var process = GitProcess.Launch(commandLine.Create(repository, arguments, environment, literalPathspecs), standardInput is not null, exited, token);
        var stdout = process.ReadAllAsync(process.StandardOutput, outputLimit, standardOutputPrefixLimit, null, heard);
        var stderr = process.ReadAllAsync(process.StandardError, outputLimit, null, standardErrorProgress is null ? null : new(standardErrorProgress), heard);
        var input = standardInput is null ? Task.CompletedTask : process.WriteInputAsync(standardInput, token);
        await Task.WhenAll(stdout, stderr, input, process.WaitForExitAsync(CancellationToken.None)).ConfigureAwait(false);
        if (process.Stopped) token.ThrowIfCancellationRequested();
        var (output, truncated) = stdout.Result;
        return new(process.ExitCode, output, stderr.Result.Bytes, truncated);
    }

    // stdout read by the caller as it arrives, which retains only its bounded page or record; stderr is drained against
    // the output limit in the background, and the input is written while the caller reads. Git is stopped before its
    // stderr is awaited, so a reader that gave up early never waits for a Git that is still writing. Both streams end
    // with Git, as a run's do, since its exit is awaited from the start.
    public async Task<T> ReadStreamAsync<T>(string root, string[] arguments,
        Func<Stream, CancellationToken, Task<(T Value, bool EndOfStream)>> read, CancellationToken ct,
        IReadOnlyDictionary<string, string?>? environment = null, byte[]? standardInput = null)
    {
        var exit = Task.CompletedTask;
        var errors = Task.CompletedTask;
        var input = Task.CompletedTask;
        try
        {
            await using var process = GitProcess.Launch(commandLine.Create(root, arguments, environment), standardInput is not null, exited, ct);
            exit = process.WaitForExitAsync(ct);
            errors = process.DiscardAsync(process.StandardError, outputLimit, ct);
            if (standardInput is not null) input = process.WriteInputAsync(standardInput, ct);
            var result = await read(process.StandardOutputUntilExit, ct).ConfigureAwait(false);
            if (!result.EndOfStream) process.Terminate();
            await Task.WhenAll(exit, errors, input).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (result.EndOfStream && process.ExitCode != 0) throw new GitCommandException(process.ExitCode);
            return result.Value;
        }
        finally
        {
            try { await exit.ConfigureAwait(false); } catch { /* Observed above, or superseded by a reader failure. */ }
            try { await errors.ConfigureAwait(false); } catch { /* Observed above, or superseded by a reader failure. */ }
            try { await input.ConfigureAwait(false); } catch { /* Observed above, or superseded by a reader failure. */ }
        }
    }
}
