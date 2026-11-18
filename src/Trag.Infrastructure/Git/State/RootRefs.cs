using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.State;

// The refs Git keeps beside HEAD while an operation runs. The files backend writes each as a file in the Git directory;
// the reftable backend keeps them in its tables, where only Git reads them (MERGE_HEAD alone stays a file there), so a
// reftable repository asks Git for all of them in one call. A ref that is not there is null; a file that holds no object
// ID is its text, which the reader then refuses.
internal sealed class RootRefs
{
    private const int FileLimit = 1024;
    private static readonly string[] Names = ["ORIG_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "REBASE_HEAD", "MERGE_AUTOSTASH"];
    private readonly Dictionary<string, string> values;

    private RootRefs(Dictionary<string, string> values) => this.values = values;

    public string? this[string name] => values.GetValueOrDefault(name);

    public static async Task<RootRefs> ReadAsync(PresetGitRunner git, string root, string gitDirectory, string commonDirectory, CancellationToken ct)
    {
        if (!Directory.Exists(Path.Combine(commonDirectory, "reftable"))) return FromFiles(gitDirectory);
        // Only the root refs themselves: Git looks a bare name up in refs/tags and refs/heads too, so a tag or a branch
        // named like one would read as an operation in progress.
        var result = await git.RunAsync(root, ["for-each-ref", "--include-root-refs", "--format=%(refname)%00%(objectname)", .. Names], ct).ConfigureAwait(false);
        return new(GitBytes.Text(result.StandardOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\0')).ToDictionary(fields => fields[0], fields => fields[1]));
    }

    public static RootRefs FromFiles(string gitDirectory) => new(Names
        .Select(name => (Name: name, Path: Path.Combine(gitDirectory, name))).Where(file => File.Exists(file.Path))
        .ToDictionary(file => file.Name, file => new FileInfo(file.Path).Length > FileLimit ? "" : File.ReadAllText(file.Path).Trim()));
}
