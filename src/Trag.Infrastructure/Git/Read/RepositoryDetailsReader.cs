using System.Globalization;
using System.Text;
using Trag.Domain.Details;
using Trag.Infrastructure.Git.Merge;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using BranchHistory = Trag.Domain.Details.BranchHistory;

namespace Trag.Infrastructure.Git.Read;

// What the repository's details show, read while the dialog is open: the current branch's history, its people as .mailmap
// names them, and what the repository holds. Every part only reads, and the parts are asked side by side.
internal sealed class RepositoryDetailsReader(GitProcessRunner runner)
{
    private readonly PresetGitRunner git = new(runner, LocalGitPreset.Writing);
    private readonly PresetGitRunner reading = new(runner, LocalGitPreset.Reading);

    public Task<RepositoryDetails> ReadAsync(string root, bool unborn, bool lfsFiles, CancellationToken ct) => ReadTimeout.RunAsync(TimeSpan.FromMinutes(2), async token =>
    {
        var history = HistoryAsync(root, unborn, token);
        var people = unborn ? Task.FromResult<IReadOnlyList<Contributor>>([]) : PeopleAsync(root, token);
        var contents = ContentsAsync(root, lfsFiles && !unborn, token);
        await Task.WhenAll(history, people, contents).ConfigureAwait(false);
        return new RepositoryDetails(await history, await people, await contents);
    }, ct);

    // Author dates, as the history shows them; a branch with more than one root dates its oldest. Every commit is what the
    // history's all-branches view holds: the branches, the remote branches, the tags and HEAD.
    private async Task<BranchHistory> HistoryAsync(string root, bool unborn, CancellationToken ct)
    {
        var all = CountAsync(root, ["rev-list", "--count", "--branches", "--remotes", "--tags", .. unborn ? Array.Empty<string>() : ["HEAD"], "--"], ct);
        if (unborn) return BranchHistory.None with { AllCommits = await all.ConfigureAwait(false) };
        var commits = CountAsync(root, ["rev-list", "--count", "HEAD", "--"], ct);
        var first = DatesAsync(root, ["log", "--no-show-signature", "--max-parents=0", "--format=%aI", "HEAD", "--"], ct);
        var latest = DatesAsync(root, ["log", "--no-show-signature", "-1", "--format=%aI", "HEAD", "--"], ct);
        await Task.WhenAll(all, commits, first, latest).ConfigureAwait(false);
        return new(await commits, await all, (await first).Min(), (await latest).Single());
    }

    private async Task<IReadOnlyList<Contributor>> PeopleAsync(string root, CancellationToken ct) =>
        ShortlogParser.Parse(Encoding.UTF8.GetString((await git.RunAsync(root, ["shortlog", "--summary", "--numbered", "--email", "--encoding=UTF-8", "HEAD", "--"], ct)
            .ConfigureAwait(false)).StandardOutput));

    private async Task<RepositoryContents> ContentsAsync(string root, bool lfsFiles, CancellationToken ct)
    {
        var refs = git.RunAsync(root, ["for-each-ref", "--format=%(refname)%00%(symref)", "refs/heads", "refs/remotes", "refs/tags"], ct);
        var stashes = git.RunAsync(root, ["stash", "list", "--format=%H"], ct);
        var submodules = SubmodulesAsync(root, ct);
        var lfs = lfsFiles ? new LfsTreeInspector(git).CountLfsAsync(root, "HEAD", ct) : Task.FromResult(0);
        var place = git.RunAsync(root, ["rev-parse", "--path-format=absolute", "--git-common-dir", "--is-shallow-repository"], ct);
        var worktrees = git.RunAsync(root, ["worktree", "list", "--porcelain", "-z"], ct);
        await Task.WhenAll(refs, stashes, submodules, lfs, place, worktrees).ConfigureAwait(false);
        var names = Lines((await refs).StandardOutput).Select(line => line.Split('\0')).Where(entry => entry[1].Length == 0).Select(entry => entry[0]).ToArray();
        var (common, shallow) = Lines((await place).StandardOutput) is [var folder, var cut] ? (folder, cut == "true") : throw new FormatException("Invalid rev-parse answer.");
        var size = await Task.Run(() => SizeOf(common), ct).ConfigureAwait(false);
        return new(Count(names, "refs/heads/"), Count(names, "refs/remotes/"), Count(names, "refs/tags/"), Lines((await stashes).StandardOutput).Length,
            await submodules, await lfs, size, shallow,
            NulRecordReader.Records((await worktrees).StandardOutput).Count(record => record.AsSpan().StartsWith("worktree "u8)) - 1);
    }

    // The gitlinks the index holds of those .gitmodules names.
    private async Task<int> SubmodulesAsync(string root, CancellationToken ct) =>
        (await SubmodulePaths.GitlinksAsync(reading, root, (await SubmodulePaths.ListedAsync(reading, root, ct).ConfigureAwait(false)).Select(submodule => submodule.Path), ct)
            .ConfigureAwait(false)).Count;

    private async Task<int> CountAsync(string root, string[] args, CancellationToken ct) =>
        int.Parse(GitBytes.Text((await git.RunAsync(root, args, ct).ConfigureAwait(false)).StandardOutput), CultureInfo.InvariantCulture);

    // An author date Git cannot read (from an import or another tool) comes back as the placeholder itself, %aI, and is left out.
    private async Task<DateTimeOffset?[]> DatesAsync(string root, string[] args, CancellationToken ct) =>
        [.. Lines((await git.RunAsync(root, args, ct).ConfigureAwait(false)).StandardOutput)
            .Select(line => DateTimeOffset.TryParse(line, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : (DateTimeOffset?)null)];

    private static string[] Lines(byte[] output) => Encoding.UTF8.GetString(output).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static int Count(string[] refs, string prefix) => refs.Count(name => name.StartsWith(prefix, StringComparison.Ordinal));

    // Every file under the Git folder, links not followed and files the user may not read left out.
    private static long SizeOf(string folder) => new DirectoryInfo(folder).EnumerateFiles("*",
        new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Sum(file => file.Length);
}
