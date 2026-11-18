namespace Trag.Application.Live;

public sealed record RepositoryChange(IReadOnlyList<string> Paths, bool Reconcile);
