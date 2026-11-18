namespace Trag.Infrastructure.Platform.Watching;

// What one filesystem event means for the status: nothing at all, one path worth re-reading, or a full re-read.
internal readonly record struct WatchDecision(WatchDecision.Kind Action, string? Path)
{
    public enum Kind { Ignore, Signal, Reconcile }

    public static WatchDecision Ignore { get; } = new(Kind.Ignore, null);
    public static WatchDecision Reconcile { get; } = new(Kind.Reconcile, null);
    public static WatchDecision Signal(string path) => new(Kind.Signal, path);
}
