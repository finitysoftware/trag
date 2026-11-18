using System.Security.Cryptography;
using System.Text;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// A repository as Git and the disk show it, read without Trag's own reader so that a check built on it does not share
// Trag's blind spots: every ref, HEAD, the index entries, the bytes of every working file, the stash entries, the local
// configuration and the operation Git has in progress. Exact names commits by id, for "nothing changed" in one
// repository; Content names a commit by its tree and subject, for "the same as the write nobody stopped" in another
// repository, whose new commits carry other times. Reflogs, ORIG_HEAD and the messages Git prepares are left out, and
// Content leaves out notes, which name the commit they annotate by id and which Git copies to a rewritten commit only
// after the rewrite, in a transaction a stop there loses as it does in a terminal.
internal sealed record RepositoryFingerprint(IReadOnlyList<string> Exact, IReadOnlyList<string> Content)
{
    private static readonly string[] Operations = ["MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply", "sequencer"];

    public static async Task<RepositoryFingerprint> ReadAsync(GitFixture f)
    {
        var exact = new List<string>();
        var content = new List<string>();
        void Add(string same) { exact.Add(same); content.Add(same); }

        foreach (var line in Lines(await f.GitAsync("for-each-ref", "--format=%(refname)%00%(objectname)%00%(tree)%00%(subject)")))
        {
            var fields = line.Split('\0');
            exact.Add("ref " + fields[0] + " " + fields[1]);
            if (!fields[0].StartsWith("refs/notes/", StringComparison.Ordinal)) content.Add("ref " + fields[0] + " " + fields[2] + " " + fields[3]);
        }
        var branch = await f.RunAsync("symbolic-ref", "-q", "HEAD");
        Add("HEAD " + (branch.ExitCode == 0 ? Encoding.UTF8.GetString(branch.StandardOutput).Trim() : "detached"));
        var head = (await f.GitAsync("log", "-1", "--format=%H%x00%T%x00%s", "HEAD")).Split('\0');
        exact.Add("HEAD " + head[0]);
        content.Add("HEAD " + head[1] + " " + head[2]);
        foreach (var entry in (await f.GitAsync("ls-files", "--stage", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries)) Add("index " + entry);
        var stashes = await f.RunAsync("log", "-g", "--format=%H%x00%T%x00%s", "refs/stash", "--");
        foreach (var line in Lines(Encoding.UTF8.GetString(stashes.StandardOutput).Trim()))
        {
            var fields = line.Split('\0');
            exact.Add("stash " + fields[0]);
            content.Add("stash " + fields[1] + " " + fields[2]);
        }
        foreach (var line in Lines(await f.GitAsync("config", "--local", "--list"))) Add("config " + line);
        foreach (var file in Directory.EnumerateFiles(f.Root, "*", SearchOption.AllDirectories)
                     .Where(path => !Path.GetRelativePath(f.Root, path).Split(Path.DirectorySeparatorChar)[0].Equals(".git", StringComparison.Ordinal)))
            Add("file " + Path.GetRelativePath(f.Root, file).Replace('\\', '/') + " " + Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file, f.Token))));
        var git = Path.Combine(f.Root, ".git");
        foreach (var operation in Operations.Where(name => File.Exists(Path.Combine(git, name)) || Directory.Exists(Path.Combine(git, name)))) Add("operation " + operation);
        var step = Path.Combine(git, "rebase-merge", "msgnum");
        if (File.Exists(step)) Add("rebase step " + (await File.ReadAllTextAsync(step, f.Token)).Trim());
        exact.Sort(StringComparer.Ordinal);
        content.Sort(StringComparer.Ordinal);
        return new(exact, content);
    }

    // The lines one side has and the other has not, for a failure message.
    public static string Difference(IReadOnlyList<string> expected, IReadOnlyList<string> actual) =>
        string.Join("; ", expected.Except(actual).Select(line => "-" + line.Replace('\0', ' ')).Concat(actual.Except(expected).Select(line => "+" + line.Replace('\0', ' '))));

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
