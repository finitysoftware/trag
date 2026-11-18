using Trag.Domain.Catalog;

namespace Trag.Infrastructure.Platform.Storage;

public sealed record RepositoryCatalogDocument(RepositoryEntry[] Repositories);
