using Trag.Domain.Stash;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.Git.Write;

// The stash list as Git reports it right now. Indices shift after a drop, so an entry is found again by id immediately
// before the command that uses it.
internal sealed class StashEntryResolver(WriteCommandRunner git)
{
    // The entry the operation names; null once it left the list.
    public async Task<StashEntry?> ResolveStashAsync(StashOperation operation, string root, CancellationToken ct) =>
        (await ListStashesAsync(root, ct).ConfigureAwait(false)).FirstOrDefault(item => item.Id == operation.Id?.Trim());

    public async Task<IReadOnlyList<StashEntry>> ListStashesAsync(string root, CancellationToken ct)
    {
        var result = await git.RunAsync(root, ["stash", "list", StashListParser.Format], ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? StashListParser.Parse(System.Text.Encoding.UTF8.GetString(result.StandardOutput)) : [];
    }

    public Task<string?> StashHeadAsync(string root, CancellationToken ct) => git.ResolveAsync(root, "refs/stash", ct);
}
