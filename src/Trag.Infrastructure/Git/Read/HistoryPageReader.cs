using System.Text;
using Trag.Domain.Errors;
using Trag.Domain.History;
using Trag.Domain.Paths;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Domain;

namespace Trag.Infrastructure.Git.Read;

// One page, or several at once, of the history behind a head (and its upstream, or every branch and tag, when asked: a
// release tagged on a branch deleted since is reached by its tag alone; an unborn HEAD has only those). The page is
// streamed and stops as soon as it is full, and it is accepted only if HEAD is still what the caller asked about, so a
// page never mixes two different histories. A file's history follows the file across renames and names it in every commit; a merge that
// brought the file's change in counts, as Git sees it against the first parent. A submodule's history lists the commits
// that moved it whatever the configuration ignores, as the status lists it.
internal sealed class HistoryPageReader(PresetGitRunner git)
{
    private static readonly string[] Follow = ["--follow", "--diff-merges=first-parent", "--root", "--name-status", "--ignore-submodules=none"];
    // The names a row shows, named, so that log.excludeDecoration hides none of them; naming them overrides it.
    private static readonly string[] Names = ["--decorate-refs=HEAD", "--decorate-refs=refs/heads/", "--decorate-refs=refs/remotes/", "--decorate-refs=refs/tags/"];

    public Task<HistoryPage> ReadPageAsync(string root, string? head, HistoryQuery request, CancellationToken ct) => ReadTimeout.RunAsync(TimeSpan.FromMinutes(2), async token =>
    {
        var (query, page, pages, allBranches) = (request.Text, request.PageIndex, request.Pages, request.AllBranches);
        if (head is not null) GitObjectId.Validate(head);
        if (request.Upstream is { } upstream) GitObjectId.Validate(upstream);
        if (page < 0 || pages < 1 || page + (long)pages > int.MaxValue / HistoryLimits.PageSize || !HistoryQuery.IsValidText(query))
            throw new ArgumentException("Invalid history query.");
        var (skip, size, last) = (page * HistoryLimits.PageSize, pages * HistoryLimits.PageSize, page + pages - 1);
        // Git pages the plain history itself; a search, and a file's history, whose --skip Git counts over every commit
        // walked, are paged here by the commits that match.
        var pagedByGit = query.Length == 0 && request.Path is null;
        string[] args = ["log", "--topo-order", "--decorate=full", .. Names, "--no-notes", "--no-show-signature", "--encoding=UTF-8", "-z", HistoryRecordFilter.Format,
            .. request.Path is null ? [] : Follow,
            .. pagedByGit ? new[] { "--skip=" + skip, "--max-count=" + (size + 1) } : [], .. allBranches ? new[] { "--branches", "--remotes", "--tags" } : request.Upstream is { } tip ? [tip] : [], .. head is null ? [] : new[] { head }, "--",
            .. request.Path is { } path ? new[] { path } : []];
        var result = await git.ReadStreamAsync(root, args, async (stream, cancel) =>
        {
            var records = new GitRecordStream(stream);
            var rows = new List<HistoryCommit>(size);
            var filter = new HistoryRecordFilter(query, pagedByGit ? 0 : skip);
            while (await records.ReadAsync(cancel, fields: HistoryRecordFilter.Fields).ConfigureAwait(false) is { } entry)
            {
                // The record is matched before the file's fields are read: reading them reuses the stream's buffer.
                var summary = filter.Match(entry.Span);
                var file = request.Path is null ? default((GitPath Path, GitPath? Original)?) : await ReadFileAsync(records, cancel).ConfigureAwait(false);
                if (summary is null) continue;
                if (rows.Count == size) return (new HistoryPage(rows, true, last), false);
                rows.Add(file is { } step ? summary with { Path = step.Path, OriginalPath = step.Original } : summary);
            }
            return (new HistoryPage(rows, false, last), true);
        }, token).ConfigureAwait(false);
        await VerifyHeadAsync(root, head, token).ConfigureAwait(false);
        return result;
    }, ct);

    // After a commit of a file's history: "\n" and its status, then the file's path, or for a rename or a copy the path
    // it came from and the one it has.
    private static async Task<(GitPath Path, GitPath? Original)?> ReadFileAsync(GitRecordStream records, CancellationToken ct)
    {
        var status = await records.ReadAsync(ct).ConfigureAwait(false) ?? throw new FormatException("Incomplete Git record.");
        var moved = status.Span.TrimStart((byte)'\n') is [(byte)'R' or (byte)'C', ..];
        var first = await ReadPathAsync(records, ct).ConfigureAwait(false);
        return moved ? (await ReadPathAsync(records, ct).ConfigureAwait(false), first) : (first, null);
    }

    private static async Task<GitPath> ReadPathAsync(GitRecordStream records, CancellationToken ct) =>
        new((await records.ReadAsync(ct).ConfigureAwait(false) ?? throw new FormatException("Incomplete Git record.")).ToArray());

    private async Task VerifyHeadAsync(string root, string? expected, CancellationToken ct)
    {
        var result = await git.ResultAsync(root, ["rev-parse", "--verify", "--quiet", "HEAD"], ct).ConfigureAwait(false);
        if ((result.ExitCode == 0 ? Encoding.ASCII.GetString(result.StandardOutput).Trim() : null) != expected)
            throw new RepositoryException(RepositoryError.ChangedDuringRead);
    }
}
