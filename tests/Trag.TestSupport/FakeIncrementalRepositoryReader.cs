using Trag.Application.Ports.Git;
using Trag.Domain.Status;

namespace Trag.TestSupport;

// A full reader with a recorded incremental path, so RepositoryWorkspace.WatchAsync can be driven through both branches.
public sealed class FakeIncrementalRepositoryReader(FakeRepositoryReader full) : IRepositoryReader, IIncrementalRepositoryReader
{
    public List<IReadOnlyList<string>> IncrementalPaths { get; } = [];

    public Task<RepositorySnapshot> ReadAsync(string path, CancellationToken cancellationToken) => full.ReadAsync(path, cancellationToken);

    public Task<RepositorySnapshot> ReadPathsAsync(RepositorySnapshot previous, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        IncrementalPaths.Add(paths);
        return Task.FromResult(full.Snapshot with { Root = previous.Root });
    }
}
