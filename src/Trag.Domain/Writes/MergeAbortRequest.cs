using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Give up the merge in progress and let Git restore the pre-merge index.
public sealed record MergeAbortRequest(RepositorySnapshot Expected) : GitWriteRequest(Expected);
