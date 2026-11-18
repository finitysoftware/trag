using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Working-tree changes thrown away. The only write that destroys content the user cannot get back from Git.
public sealed record DiscardRequest(RepositorySnapshot Expected, IReadOnlyList<GitPath> Paths) : GitWriteRequest(Expected);
