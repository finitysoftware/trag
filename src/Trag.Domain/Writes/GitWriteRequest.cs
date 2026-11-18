using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Every write carries its expected snapshot and progress. Each concrete request adds its own operation data.
public abstract record GitWriteRequest(RepositorySnapshot Expected)
{
    public IProgress<string>? Progress { get; init; }
}
