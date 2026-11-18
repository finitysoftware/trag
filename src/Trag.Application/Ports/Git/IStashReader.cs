using Trag.Domain.Stash;

namespace Trag.Application.Ports.Git;

public interface IStashReader
{
    Task<IReadOnlyList<StashEntry>> ReadStashesAsync(string root, CancellationToken ct);
}
