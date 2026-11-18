using Trag.Domain.Blame;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Domain;

namespace Trag.Infrastructure.Git.Read;

// A file blamed at a commit, or with no commit as the working tree has it, where lines not committed yet carry Git's
// all-zero id, looking past whitespace or moved lines as the options say. Git follows the file's renames on its own.
internal sealed class BlameReader(PresetGitRunner git)
{
    public Task<FileBlame> ReadBlameAsync(string root, string? revision, string path, BlameOptions options, CancellationToken ct) => ReadTimeout.RunAsync(TimeSpan.FromMinutes(2), async token =>
    {
        if (revision is not null) GitObjectId.Validate(revision);
        // A blame.ignoreRevsFile set for every repository names a file only some of them have, and Git refuses to blame
        // where one is missing: the list is cleared and only the files that are here are named again.
        var configured = await git.RunAsync(root, ["config", "--get-all", "--path", "blame.ignoreRevsFile"], token, allowMissing: true).ConfigureAwait(false);
        var ignoreRevs = GitBytes.Text(configured.StandardOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(file => File.Exists(Path.Combine(root, file))).Select(file => "--ignore-revs-file=" + file);
        // Git prints each commit's author and subject in i18n.logOutputEncoding unless it is asked for UTF-8.
        var blame = await git.RunAsync(root, ["-c", "core.quotePath=false", "blame", "--porcelain", "--encoding=UTF-8", "--no-ignore-revs-file", .. ignoreRevs,
            .. options.IgnoreWhitespace ? new[] { "-w" } : [],
            .. options.DetectMoves ? new[] { "-C" } : [], .. revision is null ? [] : new[] { revision }, "--", path],
            token).ConfigureAwait(false);
        return BlamePorcelain.Parse(blame.StandardOutput);
    }, ct);
}
