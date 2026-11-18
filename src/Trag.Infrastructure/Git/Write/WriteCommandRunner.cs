using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// Every write command runs the same way: no lazy fetch, no configured protocols, no fsmonitor, and against the private
// index of a transaction when there is one. The reflog action is how a commit is found again afterwards; a command that
// must not open an editor sets its own variables.
internal sealed class WriteCommandRunner(GitProcessRunner runner)
{
    private readonly PresetGitRunner writing = new(runner, LocalGitPreset.Writing);

    // The object a revision names, or null when Git cannot resolve it.
    // Git opens no editor: a message it would ask for is the one it prepared.
    public static readonly IReadOnlyDictionary<string, string?> NoEditor = new Dictionary<string, string?> { ["GIT_EDITOR"] = ":" };

    public async Task<string?> ResolveAsync(string root, string revision, CancellationToken ct)
    {
        var result = await RunAsync(root, ["rev-parse", "--verify", "--quiet", revision], ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? GitBytes.Text(result.StandardOutput) : null;
    }

    public async Task<bool> RefExistsAsync(string root, string reference, CancellationToken ct) =>
        await ResolveAsync(root, reference, ct).ConfigureAwait(false) is not null;

    public Task<GitResult> RunAsync(string root, string[] args, CancellationToken ct, IndexTransaction? transaction = null, byte[]? input = null, string? action = null,
        bool literalPathspecs = true, IReadOnlyDictionary<string, string?>? variables = null)
    {
        var environment = new Dictionary<string, string?>(variables ?? new Dictionary<string, string?>());
        if (transaction is not null) environment["GIT_INDEX_FILE"] = transaction.TemporaryIndex;
        if (action is not null) environment["GIT_REFLOG_ACTION"] = action;
        return writing.ResultAsync(root, args, ct, environment, input, literalPathspecs);
    }
}
