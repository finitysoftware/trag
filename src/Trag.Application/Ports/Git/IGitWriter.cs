using Trag.Domain.Writes;

namespace Trag.Application.Ports.Git;

public interface IGitWriter
{
    Task<GitWriteResult> ExecuteAsync(GitWriteRequest request, CancellationToken ct);
}
