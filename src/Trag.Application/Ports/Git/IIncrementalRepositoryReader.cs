using Trag.Domain.Status;

namespace Trag.Application.Ports.Git;

public interface IIncrementalRepositoryReader
{
    Task<RepositorySnapshot> ReadPathsAsync(RepositorySnapshot previous, IReadOnlyList<string> paths, CancellationToken ct);
}
