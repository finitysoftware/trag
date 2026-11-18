using System.Text;
using Trag.Application.Ports.Git;
using Trag.Domain.Stash;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

public sealed class CliStashReader(GitProcessRunner runner) : IStashReader
{
    public Task<IReadOnlyList<StashEntry>> ReadStashesAsync(string root, CancellationToken ct) => ReadTimeout.RunAsync(TimeSpan.FromSeconds(30), async token =>
    {
        // A stash's message reads in UTF-8 whatever encoding i18n.logOutputEncoding asks Git to print in.
        var result = await new PresetGitRunner(runner, LocalGitPreset.Writing).RunAsync(root, ["stash", "list", "--encoding=UTF-8", StashListParser.Format], token).ConfigureAwait(false);
        return StashListParser.Parse(Encoding.UTF8.GetString(result.StandardOutput));
    }, ct);
}
