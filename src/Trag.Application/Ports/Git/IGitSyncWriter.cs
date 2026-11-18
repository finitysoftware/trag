using Trag.Domain.Writes;

namespace Trag.Application.Ports.Git;

public interface IGitSyncWriter
{
    Task<GitWriteResult> ExecuteAsync(GitWriteRequest request, CancellationToken ct);
}
