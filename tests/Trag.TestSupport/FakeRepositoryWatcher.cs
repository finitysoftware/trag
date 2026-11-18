using Trag.Application.Ports.Watching;
using Trag.Domain.Status;

namespace Trag.TestSupport;

// Hands out FakeChangeSource instances; the test pushes changes into the latest one. Concurrent reads call Watch from
// thread-pool threads, so the list is guarded.
public sealed class FakeRepositoryWatcher : IRepositoryWatcher
{
    private readonly object sync = new();
    private readonly List<FakeChangeSource> sources = [];
    public IReadOnlyList<FakeChangeSource> Sources { get { lock (sync) return sources.ToArray(); } }
    public FakeChangeSource Latest => Sources[^1];
    public Exception? Failure { get; set; }

    public IRepositoryChangeSource Watch(RepositorySnapshot repository)
    {
        if (Failure is not null) throw Failure;
        var source = new FakeChangeSource();
        lock (sync) sources.Add(source);
        return source;
    }
}
