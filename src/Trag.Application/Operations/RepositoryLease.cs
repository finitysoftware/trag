namespace Trag.Application.Operations;

// The identity of one opened repository and its version: a read or write takes the version before it starts and
// asks afterwards whether it still speaks for the current state. Every mutation moves the version on.
public sealed class RepositoryLease(string path)
{
    private long version;
    public string Path { get; } = path;
    public long Version => Interlocked.Read(ref version);
    public long Invalidate() => Interlocked.Increment(ref version);
    public bool IsCurrent(long requestVersion) => Version == requestVersion;
}
