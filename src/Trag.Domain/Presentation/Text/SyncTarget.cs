using Trag.Domain.Status;
using Trag.Domain.Sync;

namespace Trag.Domain.Presentation.Text;

// Where sync fetches from and pushes to, and the warning when a push goes into a branch of another name.

public sealed record SyncTarget(string FetchAddress, string PushDestination, string Warning)
{
    public bool HasWarning => Warning.Length != 0;

    public static SyncTarget Empty(RepositoryRemote? remote) =>
        new(remote?.FetchAddress ?? "Choose a remote", "Choose a remote and upstream branch", "");

    public static SyncTarget From(RepositorySnapshot snapshot, RepositorySyncState state) =>
        new(state.Remote.FetchAddress, Destination(snapshot, state), PushWarning(snapshot, state));

    private static string Destination(RepositorySnapshot snapshot, RepositorySyncState state)
    {
        if (state.Branch is null) return "Choose an upstream branch";
        return snapshot.Branch + " → " + state.Branch.SourceRef + "\n" + state.Remote.PushAddress;
    }

    private static string PushWarning(RepositorySnapshot snapshot, RepositorySyncState state)
    {
        if (state.Branch is not { IsNew: false } branch || branch.Display == snapshot.Branch) return "";
        return "Push sends " + snapshot.Branch + " into " + state.Remote.Name + "/" + branch.Display + ".";
    }
}
