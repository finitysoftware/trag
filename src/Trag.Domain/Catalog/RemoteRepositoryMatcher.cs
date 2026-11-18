using Trag.Domain.Naming;

namespace Trag.Domain.Catalog;

// Pairs a hosted repository with the local repository whose origin has the same address, whatever form each URL takes.
public sealed class RemoteRepositoryMatcher<T> where T : class
{
    private readonly Dictionary<string, T> byOrigin = new(StringComparer.Ordinal);

    public RemoteRepositoryMatcher(IEnumerable<T> locals, Func<T, string?> originOf)
    {
        foreach (var local in locals)
        {
            var origin = RemoteUrls.Normalize(originOf(local));
            if (origin.Length != 0) byOrigin.TryAdd(origin, local);
        }
    }

    public T? Find(string cloneUrl) => byOrigin.GetValueOrDefault(RemoteUrls.Normalize(cloneUrl));
}
