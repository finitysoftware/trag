namespace Trag.Domain.Presentation.Text;

// What a diff pane says instead of lines: nothing chosen, reading, the repository moved, interrupted, failed.

public sealed record DiffMessages(string Empty, string Loading, string Changed, string Cancelled, string Failed)
{
    public static DiffMessages Working { get; } = new(
        "Select a file to see its changes.",
        "Loading changes…",
        "Repository changed · waiting for current status…",
        "Preview interrupted. Select the file again or refresh.",
        "This file could not be read. It may have changed or become unavailable. Refresh to retry.");

    public static DiffMessages Commit { get; } = new(
        "Select a changed file to see its diff.",
        "Loading commit diff…",
        "Repository changed. Select a current commit.",
        "Commit diff canceled.",
        "The commit diff could not be read. Select the file again to retry.");
}
