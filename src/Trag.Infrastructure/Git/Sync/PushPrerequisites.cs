using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;

namespace Trag.Infrastructure.Git.Sync;

// What the remote needs before a push's commits arrive: the submodule commits they record, which are only checked for, and
// the content of their Git LFS files, which goes first as its own transfer. A refusal says which of them is missing.
internal sealed class PushPrerequisites(UnpushedSubmodules submodules, LfsTransfer lfs)
{
    public async Task<GitWriteResult?> MeetAsync(string root, RemoteConfiguration config, string head, IProgress<string>? progress, CancellationToken ct)
    {
        if (await submodules.FindAsync(root, head, config.Remote.Name, ct).ConfigureAwait(false) is { Count: > 0 } unpushed)
            return GitWriteResult.Rejected(TransportText.SubmodulesNotPushed(unpushed));
        return await lfs.UploadAsync(root, config, head, progress, ct).ConfigureAwait(false) is { } failed
            ? GitWriteResult.Rejected(SyncOutcomeText.LfsNotPushed, GitOutputText.Details(failed))
            : null;
    }
}
