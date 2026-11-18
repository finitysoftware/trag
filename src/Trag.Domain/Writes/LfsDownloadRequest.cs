using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// The Git LFS content of a commit from a remote branch, downloaded with that remote's credentials before a merge or a new
// branch checks the commit out; NotDownloaded is what the user reads when it does not come down.
public sealed record LfsDownloadRequest(RepositorySnapshot Expected, string Remote, string Commit, string NotDownloaded = MergeOutcomeText.LfsNotDownloaded)
    : GitWriteRequest(Expected);
