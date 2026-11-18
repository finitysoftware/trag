using Trag.Domain.Status;

namespace Trag.Application.Ports.Git;

public interface IRepositoryReader
{
    Task<RepositorySnapshot> ReadAsync(string path, CancellationToken ct);
}
