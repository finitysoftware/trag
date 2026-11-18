using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// Git LFS's storage of the content its pointers stand for: lfs.storage when it is set, relative to the Git directory
// unless it is absolute, else lfs there.
internal sealed class LfsStorage(PresetGitRunner git)
{
    public async Task<string> ObjectsAsync(string root, CancellationToken ct)
    {
        var common = GitBytes.Text((await git.RunAsync(root, ["rev-parse", "--path-format=absolute", "--git-common-dir"], ct).ConfigureAwait(false)).StandardOutput);
        var configured = await git.ResultAsync(root, ["config", "--get", "lfs.storage"], ct).ConfigureAwait(false);
        return Path.Combine(common, configured.ExitCode == 0 ? GitBytes.Text(configured.StandardOutput) : "lfs", "objects");
    }

    // Whether any content is stored here at all.
    public async Task<bool> HoldsContentAsync(string root, CancellationToken ct)
    {
        var objects = await ObjectsAsync(root, ct).ConfigureAwait(false);
        return Directory.Exists(objects) && Directory.EnumerateFiles(objects, "*", SearchOption.AllDirectories).Any();
    }
}
