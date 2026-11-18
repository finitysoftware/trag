namespace Trag.ViewModels.Sync;

// One remote in the remote picker, the chosen one marked.
public sealed record SyncRemoteRow(string Name, bool IsChosen);
