using Trag.Domain.Status;
using Trag.Domain.Sync;

namespace Trag.Domain.Writes;

// A transfer against the displayed target, or the saving of which account that target uses. The synchronization state
// is the one the user was shown, including the configuration fingerprint the guard compares against.
public sealed record SyncRequest(RepositorySnapshot Expected, RepositorySyncState Sync, SyncAction Action, string? AccountId = null) : GitWriteRequest(Expected);
