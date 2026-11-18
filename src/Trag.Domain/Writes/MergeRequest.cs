using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// A branch merged into the current one: the commit the dialog previewed, named for the status line, whether Git must
// create a merge commit even where it could fast-forward, and the message of that commit.
public sealed record MergeRequest(RepositorySnapshot Expected, string FromName, string FromHead, bool NoFastForward, string Message) : GitWriteRequest(Expected);
