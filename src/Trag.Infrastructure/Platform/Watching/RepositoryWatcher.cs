using Trag.Application.Ports.Watching;
using Trag.Domain.Status;

namespace Trag.Infrastructure.Platform.Watching;

// One source per repository the shell opens; the source itself owns the watchers and lives until it is disposed. The
// user's own Git configuration is watched too, where Git reads it unless the caller names the files.
public sealed class RepositoryWatcher(IReadOnlyList<string>? configFiles = null) : IRepositoryWatcher
{
    public IRepositoryChangeSource Watch(RepositorySnapshot repository) =>
        new RepositoryChangeSource(repository, configFiles ?? GlobalConfigFiles.Of(Environment.GetEnvironmentVariable));
}
