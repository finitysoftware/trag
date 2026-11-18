using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Whole files out of the index, leaving the working tree alone.
public sealed record UnstageRequest(RepositorySnapshot Expected, IReadOnlyList<GitPath> Paths) : GitWriteRequest(Expected);
