using Trag.Domain.Status;

namespace Trag.Application.Ports.Watching;

public interface IRepositoryWatcher
{
    IRepositoryChangeSource Watch(RepositorySnapshot repository);
}
