using System.Text;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Sync;

// The submodules whose commits a push would record although no remote of the submodule has them, found as Git's
// push.recurseSubmodules=check finds them but over the commits the remote really lacks: those the remote's own tracking
// refs do not reach, whatever remote the transfer itself runs against. A submodule that is not here, that has no
// remote-tracking refs to compare with, or that does not have the commit, is left alone, as Git leaves it. Without .gitmodules nothing is asked, and each submodule is asked once
// about all its commits, however long the history the remote lacks.
internal sealed class UnpushedSubmodules(PresetGitRunner git)
{
    public async Task<IReadOnlyList<string>> FindAsync(string root, string head, string remote, CancellationToken ct)
    {
        if (!File.Exists(Path.Combine(root, ".gitmodules"))) return [];
        var recorded = await git.StreamAsync(root, ["log", "--no-show-signature", "--format=", "--raw", "-z", "--no-abbrev", "--no-renames", "--ignore-submodules=none", "--root", "-m", head, "--not", "--remotes=" + remote],
            ReadGitlinksAsync, ct).ConfigureAwait(false);
        var unpushed = new List<string>();
        foreach (var commits in recorded.GroupBy(gitlink => gitlink.Path, gitlink => gitlink.Commit))
        {
            var submodule = Path.Combine(root, commits.Key);
            if (!Path.Exists(Path.Combine(submodule, ".git"))
                || (await git.ResultAsync(submodule, ["for-each-ref", "--count=1", "--format=%(refname)", "refs/remotes"], ct).ConfigureAwait(false)).StandardOutput.Length == 0)
                continue;
            var reachable = await git.ResultAsync(submodule, ["rev-list", "-n", "1", "--ignore-missing", "--stdin", "--not", "--remotes"], ct,
                input: Encoding.ASCII.GetBytes(string.Concat(commits.Select(commit => commit + "\n")))).ConfigureAwait(false);
            if (reachable.ExitCode == 0 && reachable.StandardOutput.Length != 0) unpushed.Add(commits.Key);
        }
        return unpushed;
    }

    // git log --raw -z gives each change as ":<old mode> <new mode> <old id> <new id> <status>" and then its path.
    private static async Task<List<(string Path, string Commit)>> ReadGitlinksAsync(Stream output, CancellationToken ct)
    {
        var records = new GitRecordStream(output);
        var gitlinks = new List<(string Path, string Commit)>();
        while (await records.ReadAsync(ct) is { } record)
        {
            if (record.Length == 0 || record.Span[0] != (byte)':') continue;
            var fields = Encoding.ASCII.GetString(record.Span).Split(' ');
            if (await records.ReadAsync(ct) is { } path && fields is [_, "160000", _, var commit, _])
                gitlinks.Add((Encoding.UTF8.GetString(path.Span), commit));
        }
        return gitlinks;
    }
}
