using Trag.Domain.Catalog;

namespace Trag.Application.Ports.Catalog;

public interface IRepositoryProber
{
    // Checks whether the stored link still points at a working copy and reads its branch and origin address.
    Task<RepositoryProbe> ProbeAsync(string path, CancellationToken ct);
}
