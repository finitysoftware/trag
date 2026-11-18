namespace Trag.Domain.Catalog;

// What a link in the catalog points at when it was last checked: a working copy, nothing, a folder that is not a
// repository, one Git does not trust for its ownership, one in a format only a newer Git reads, a bare repository,
// which has no working tree, or a folder of a repository's Git data, such as a working copy's .git folder; Unknown until
// it is checked.
public enum RepositoryLinkStatus { Unknown, Valid, Missing, NotRepository, Untrusted, NeedsNewerGit, Bare, GitDirectory }
