using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// A file of the working tree given the content it has in a commit; the index is left as it is.
public sealed record RestoreFileRequest(RepositorySnapshot Expected, string Commit, GitPath Path) : GitWriteRequest(Expected);
