namespace Trag.Infrastructure.Git.Processes;

// A Git call with one of the local presets applied: its configuration before the command's own arguments, and its
// variables under any the caller adds (a private index, a reflog action, an editor that must not open). ResultAsync
// answers with Git's own exit code; RunAsync takes a non-zero exit with whole output as a failure, while output cut at
// a prefix limit is an answer, and so is exit 1 where the caller says a missing value is one.
internal sealed class PresetGitRunner(GitProcessRunner runner, LocalGitPreset preset)
{
    public string Program => runner.Program;

    public Task<T> ReadStreamAsync<T>(string root, string[] arguments,
        Func<Stream, CancellationToken, Task<(T Value, bool EndOfStream)>> read, CancellationToken ct, byte[]? input = null) =>
        runner.ReadStreamAsync(root, preset.Arguments(arguments), read, ct, preset.Variables, input);

    // Required output that may be larger than the runner holds, read to its end as it arrives.
    public Task<T> StreamAsync<T>(string root, string[] arguments, Func<Stream, CancellationToken, Task<T>> readToEnd, CancellationToken ct, byte[]? input = null) =>
        ReadStreamAsync(root, arguments, async (stream, cancel) => (await readToEnd(stream, cancel).ConfigureAwait(false), true), ct, input);

    public Task<GitResult> ResultAsync(string root, string[] arguments, CancellationToken ct,
        IReadOnlyDictionary<string, string?>? variables = null, byte[]? input = null, bool literalPathspecs = true, int? prefix = null) =>
        runner.RunAsync(root, preset.Arguments(arguments), ct, With(variables), input, prefix, literalPathspecs: literalPathspecs);

    public async Task<GitResult> RunAsync(string root, string[] arguments, CancellationToken ct, byte[]? input = null, int? prefix = null,
        IReadOnlyDictionary<string, string?>? variables = null, bool allowMissing = false, bool literalPathspecs = true)
    {
        var result = await runner.RunAsync(root, preset.Arguments(arguments), ct, With(variables), input, prefix, literalPathspecs: literalPathspecs).ConfigureAwait(false);
        if (result.ExitCode != 0 && !result.OutputTruncated && !(allowMissing && result.ExitCode == 1)) throw new GitCommandException(result.ExitCode);
        return result;
    }

    private IReadOnlyDictionary<string, string?> With(IReadOnlyDictionary<string, string?>? variables)
    {
        if (variables is null) return preset.Variables;
        var all = new Dictionary<string, string?>(preset.Variables);
        foreach (var (name, value) in variables) all[name] = value;
        return all;
    }
}
