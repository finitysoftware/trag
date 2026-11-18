using System.Globalization;
using System.Text;
using Trag.Domain.History;
using Trag.Infrastructure.Git.Processes;
using Trag.Domain;

namespace Trag.Infrastructure.Git.Read;

// What the merge and the rebase previews read the same way: the commits two refs point at, how many commits only each
// side has, and commits as the history shows them.
internal static class PreviewReads
{
    public static async Task<(string Left, string Right)> HeadsAsync(PresetGitRunner git, string root, string left, string right, CancellationToken ct)
    {
        var heads = Text(await git.RunAsync(root, ["rev-parse", left + "^{commit}", right + "^{commit}"], ct).ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (heads.Length != 2) throw new FormatException("Expected two commits.");
        GitObjectId.Validate(heads[0]);
        GitObjectId.Validate(heads[1]);
        return (heads[0], heads[1]);
    }

    public static async Task<(int LeftOnly, int RightOnly)> CountsAsync(PresetGitRunner git, string root, string left, string right, CancellationToken ct)
    {
        var counts = Text(await git.RunAsync(root, ["rev-list", "--left-right", "--count", left + "..." + right, "--"], ct).ConfigureAwait(false)).Split('\t');
        if (counts.Length != 2) throw new FormatException("Expected two commit counts.");
        return (Number(counts[0]), Number(counts[1]));
    }

    // The selection names the commits and their order (a range, or listed ids); at most capacity are expected.
    public static Task<IReadOnlyList<HistoryCommit>> CommitsAsync(PresetGitRunner git, string root, string[] selection, int capacity, CancellationToken ct) =>
        git.ReadStreamAsync(root, ["log", "--no-decorate", "--no-notes", "--no-show-signature", "--encoding=UTF-8", "-z", CommitRecord.Format, .. selection, "--"],
            async (stream, token) =>
            {
                var records = new GitRecordStream(stream);
                var commits = new List<HistoryCommit>(capacity);
                while (await records.ReadAsync(token, fields: CommitRecord.Fields).ConfigureAwait(false) is { } record)
                    commits.Add(CommitRecord.Parse(record.Span).Summary);
                return ((IReadOnlyList<HistoryCommit>)commits, true);
            }, ct);

    public static int Number(string text) => int.Parse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
    public static string Text(GitResult result) => Encoding.UTF8.GetString(result.StandardOutput).Trim();
}
