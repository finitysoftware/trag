using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Merge;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;

namespace Trag.Infrastructure.Git.Sync;

// The content of a commit's Git LFS files moved as a transfer of its own, with the remote's credentials and limits: sent
// before a push or a publish sends the commit, fetched before a pull merges it. Git LFS sends the content of every commit
// the remote lacks, the ones before the last too, so a push asks it whenever the repository stores content at all; a
// fetch asks it only for a commit that has LFS files. What comes back is Git LFS's failed run, or null.
internal sealed class LfsTransfer(GitTransport transport, LfsTreeInspector lfsFiles, LfsStorage storage)
{
    public async Task<GitResult?> UploadAsync(string root, RemoteConfiguration config, string commit, IProgress<string>? progress, CancellationToken ct) =>
        await storage.HoldsContentAsync(root, ct).ConfigureAwait(false)
            ? await RunAsync(root, config, commit, "push", SyncOutcomeText.PushingLfs, progress, ct).ConfigureAwait(false) : null;

    public async Task<GitResult?> DownloadAsync(string root, RemoteConfiguration config, string commit, IProgress<string>? progress, CancellationToken ct) =>
        await lfsFiles.HasLfsAsync(root, commit, ct).ConfigureAwait(false)
            ? await FetchAsync(root, config, commit, progress, ct).ConfigureAwait(false) : null;

    // The download for a commit the caller already knows has LFS files.
    public Task<GitResult?> FetchAsync(string root, RemoteConfiguration config, string commit, IProgress<string>? progress, CancellationToken ct) =>
        RunAsync(root, config, commit, "fetch", SyncOutcomeText.FetchingLfs, progress, ct);

    private async Task<GitResult?> RunAsync(string root, RemoteConfiguration config, string commit, string command, string stage,
        IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report(stage);
        var result = await transport.TransportAsync(root, config, "lfs", [command], [commit], ct, progress, pushEndpoint: command == "push").ConfigureAwait(false);
        return result.ExitCode == 0 ? null : result;
    }
}
