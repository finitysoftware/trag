using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// A commit of the history undone by a new commit on the current branch, with Git's own message; the reverted commit
// stays where it is.
public sealed record RevertRequest(RepositorySnapshot Expected, string Commit) : GitWriteRequest(Expected);
