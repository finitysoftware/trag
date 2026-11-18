namespace Trag.Domain.Writes;

// What a write needs from the repository besides no other write running: a repository with no operation in progress,
// a merge Trag can continue, a rebase, a revert or a cherry-pick it can continue, any of them for resolving a conflict,
// or nothing at all.
public enum WriteNeed { Writable, Merge, Rebase, Pick, Resolution, Nothing }
