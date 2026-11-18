using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// The current branch replayed onto the commit the dialog previewed, named for the status line.
public sealed record RebaseRequest(RepositorySnapshot Expected, string OntoName, string OntoHead) : GitWriteRequest(Expected);
