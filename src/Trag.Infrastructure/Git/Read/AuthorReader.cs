using System.Globalization;
using System.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// The authors of the newest commits as .mailmap names them, one per commit and newest first: the people a co-author is
// chosen from.
internal sealed class AuthorReader(PresetGitRunner git)
{
    private const int Limit = 2000;

    public async Task<IReadOnlyList<GitIdentity>> ReadAuthorsAsync(string root, CancellationToken ct)
    {
        var log = await git.RunAsync(root, ["log", "-n", Limit.ToString(CultureInfo.InvariantCulture), "--no-show-signature", "--encoding=UTF-8", "--format=%aN%x00%aE", "HEAD", "--"], ct).ConfigureAwait(false);
        return [.. Encoding.UTF8.GetString(log.StandardOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\0')).Select(parts => new GitIdentity(parts[0], parts[1]))];
    }
}
