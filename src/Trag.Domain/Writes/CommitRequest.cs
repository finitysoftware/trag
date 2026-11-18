using System.Text;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Staged content into a commit. FinishesMerge says this commit closes a merge in progress, which changes what has to
// be true first: every conflict resolved, and both parents verified afterwards. Amend replaces the last commit instead,
// with this message and whatever is staged on top of it, on that commit's own parents.
public sealed record CommitRequest(RepositorySnapshot Expected, string Message, bool FinishesMerge = false, bool Amend = false) : GitWriteRequest(Expected)
{
    // A commit needs an author and a committer Git knows, and a message it accepts.
    public static bool CanCommit(RepositorySnapshot? snapshot, string message) => snapshot is { HasCommitIdentity: true } && IsValidMessage(message);

    // Why a message written is not one Git is offered, for the line under the editor; nothing while it is still empty.
    public static string MessageHint(string message) =>
        string.IsNullOrWhiteSpace(message) ? ""
        : CoAuthors.LacksSubject(message) ? WriteText.SubjectMissing
        : IsValidMessage(message) ? "" : WriteText.CommitMessageInvalid;

    // What a message must be before it is offered to Git: not blank, not only co-author trailers, no NUL, at most 64 KB of UTF-8.
    public static bool IsValidMessage(string message) =>
        !string.IsNullOrWhiteSpace(message) && !CoAuthors.LacksSubject(message) && !message.Contains('\0') && Encoding.UTF8.GetByteCount(message) <= GitLimits.CommitMessageBytes;
}
