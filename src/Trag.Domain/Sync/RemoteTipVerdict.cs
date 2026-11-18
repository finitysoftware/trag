namespace Trag.Domain.Sync;

// Arrived: the remote holds what the transfer was meant to leave. Interrupted: it is unchanged and the transfer never
// finished. Refused: it is unchanged and Git said no. Changed: it holds something else, and only a fetch can say what.
public enum RemoteTipVerdict { Arrived, Interrupted, Refused, Changed }
