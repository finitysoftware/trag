namespace Trag.Domain.Stash;

// Message names a new stash (empty leaves Git's default); IncludeUntracked adds new files; Id selects the entry to apply or drop.
public sealed record StashOperation(string? Message = null, bool IncludeUntracked = false, string? Id = null);
