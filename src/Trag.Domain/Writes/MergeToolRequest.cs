using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Open one conflicted file in the user's own tool and wait for it. This holds the mutation queue for as long as the
// tool is open, on purpose: nothing else may touch the index while a tool has the file. Editor picks the configured
// editor rather than the merge tool.
public sealed record MergeToolRequest(RepositorySnapshot Expected, GitPath Path, bool Editor) : GitWriteRequest(Expected);
