using Trag.Domain.Paths;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// A commit's version of a file written where the user chose, as a checkout would give it: Git's filters and line
// endings apply. Git writes into a new file beside the chosen one, which takes its place only once Git has given all of
// it, so a file already there stays whole when Git cannot.
internal sealed class FileVersionReader(PresetGitRunner git)
{
    public async Task SaveAsync(string root, string commit, GitPath path, string destination, CancellationToken ct)
    {
        var partial = Path.Combine(Path.GetDirectoryName(destination)!, Path.GetRandomFileName());
        try
        {
            await using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await git.ReadStreamAsync(root, ["cat-file", "--filters", commit + ":" + path.Display], async (output, cancel) =>
                {
                    await output.CopyToAsync(file, cancel).ConfigureAwait(false);
                    return (true, true);
                }, ct).ConfigureAwait(false);
            File.Move(partial, destination, overwrite: true);
        }
        catch
        {
            File.Delete(partial);
            throw;
        }
    }
}
