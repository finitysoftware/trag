namespace Trag.Domain.Catalog;

// An account's hosted repositories as Trag read them. ReachedLimit says Trag read every page it reads and the last one
// was full, so the account may hold older repositories than these.
public sealed record RemoteRepositoryList(IReadOnlyList<RemoteRepository> Repositories, bool ReachedLimit);
