namespace Trag.Domain.Merge;

// A conflicted working file read for the merge view: its text, the hash of its bytes (a choice made on it is written
// only over the same bytes) and the length of the markers Git wrote in it, or why it cannot be shown as text.
public sealed record ConflictText(string Text, string Hash, string Message = "")
{
    public int MarkerSize { get; init; } = ConflictDocument.DefaultMarkerSize;
    // The largest working file the merge view reads whole.
    public const int ByteLimit = 2 * 1024 * 1024;
    public bool IsReadable => Message.Length == 0;
    public static ConflictText Unavailable(string message) => new("", "", message);
}
