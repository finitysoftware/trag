using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// What one commit did to one file undone in the working tree; a renamed file is named by both its names.
public sealed record RevertFileRequest(RepositorySnapshot Expected, string Commit, GitPath Path, GitPath? OriginalPath) : GitWriteRequest(Expected);
