namespace Trag.Domain.Writes;

// ForcePush replaces the remote branch with a rewritten one, only while it is still where the last fetch saw it.
public enum SyncAction { Fetch, Pull, Push, SelectAccount, ForcePush }
