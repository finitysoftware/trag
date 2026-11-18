using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// The name and address Git itself would stamp on a commit. Git refuses to build one from an empty name or address, and
// a repository without an identity is a fact the shell shows rather than a failed read. A name that is not UTF-8, saved by
// an editor in another code page, is shown as far as it reads: Git commits its own bytes, never these.
internal sealed class GitIdentityReader(PresetGitRunner git)
{
    public async Task<GitIdentity?> ReadAsync(string root, string name, CancellationToken ct)
    {
        var result = await git.ResultAsync(root, ["var", name], ct).ConfigureAwait(false);
        if (result.ExitCode != 0) return null;
        var ident = GitBytes.Text(result.StandardOutput);
        var end = ident.LastIndexOf('>');
        var start = end < 0 ? -1 : ident.LastIndexOf('<', end);
        return start > 0 && end > start + 1 ? new(ident[..start].TrimEnd(), ident[(start + 1)..end]) : null;
    }
}
