using System.Text;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// The submodules a repository names: what its .gitmodules gives for each, and of those the ones its index holds as
// gitlinks, since .gitmodules can still name a submodule the index no longer has. Without .gitmodules Git is not asked.
internal static class SubmodulePaths
{
    public sealed record Submodule(string Name, string Path, string? Update);

    public static async Task<Submodule[]> ListedAsync(PresetGitRunner git, string root, CancellationToken ct)
    {
        if (!File.Exists(System.IO.Path.Combine(root, ".gitmodules"))) return [];
        var listed = await git.ResultAsync(root, ["config", "--file", ".gitmodules", "--null", "--get-regexp", "^submodule\\..*\\.(path|update)$"], ct).ConfigureAwait(false);
        var entries = new GitConfiguration(GitConfigParser.Parse(listed.StandardOutput));
        return [.. entries.Entries.Where(entry => entry.Key.EndsWith(".path", StringComparison.Ordinal))
            .Select(entry => entry.Key[10..^5]).Select(name => new Submodule(name, entries.Value("submodule." + name + ".path")!, entries.Value("submodule." + name + ".update")))];
    }

    public static async Task<IReadOnlyList<string>> GitlinksAsync(PresetGitRunner git, string root, IEnumerable<string> paths, CancellationToken ct)
    {
        var gitlinks = new List<string>();
        foreach (var batch in PathBatches.Of(paths)) gitlinks.AddRange(await GitlinksOfAsync(git, root, batch, literal: true, ct).ConfigureAwait(false));
        return gitlinks;
    }

    // The gitlinks Git's own pathspecs match, as submodule.active gives them.
    public static Task<IReadOnlyList<string>> MatchingAsync(PresetGitRunner git, string root, string[] pathspecs, CancellationToken ct) =>
        GitlinksOfAsync(git, root, pathspecs, literal: false, ct);

    private static async Task<IReadOnlyList<string>> GitlinksOfAsync(PresetGitRunner git, string root, string[] pathspecs, bool literal, CancellationToken ct)
    {
        var indexed = await git.ResultAsync(root, ["ls-files", "--stage", "-z", "--", .. pathspecs], ct, literalPathspecs: literal).ConfigureAwait(false);
        return [.. NulRecordReader.Records(indexed.StandardOutput).Where(row => GitEntries.IsGitlink(row))
            .Select(row => Encoding.UTF8.GetString(row.AsSpan(Array.IndexOf(row, (byte)'\t') + 1)))];
    }
}
