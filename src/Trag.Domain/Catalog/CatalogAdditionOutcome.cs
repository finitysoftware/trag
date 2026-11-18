namespace Trag.Domain.Catalog;

// What became of a folder offered to the catalog: it joined the list, it was there already, it could not join because it
// is gone, is not a Git repository, is one Git does not trust for its ownership, is in a format only a newer Git reads,
// is a bare repository, holds a repository's Git data or the list is full, the offer was cancelled, or reading,
// initializing or saving failed.
public enum CatalogAdditionOutcome { Added, AlreadyListed, Missing, NotRepository, Untrusted, NeedsNewerGit, Bare, GitDirectory, Full, Canceled, Failed }
