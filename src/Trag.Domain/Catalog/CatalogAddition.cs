namespace Trag.Domain.Catalog;

// The outcome of offering a folder to the catalog; the entry is the one listed when the folder was accepted.
public sealed record CatalogAddition(CatalogAdditionOutcome Outcome, RepositoryEntry? Entry);
