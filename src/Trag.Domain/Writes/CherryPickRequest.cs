using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// A commit of the history applied again by a new commit on the current branch, with its own message; the commit itself
// stays where it is.
public sealed record CherryPickRequest(RepositorySnapshot Expected, string Commit) : GitWriteRequest(Expected);
