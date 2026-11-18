using Trag.Domain.Blame;

namespace Trag.Domain.Presentation.Text;

// What the blame view says: which version of the file it blames, the commit on the first line of each block by its short
// id, author and age, the chosen block's commit by its short id and subject, that the file could not be blamed or is
// binary, and that the repository moved while it was read.
public static class BlameText
{
    public const string NotCommitted = "Not committed yet";
    public const string NotRead = "This file could not be blamed.";
    public const string NotDownloaded = "This file could not be blamed. This is a partial clone, and earlier versions of the file may not be downloaded; Trag does not fetch them.";
    public const string Changed = "The repository changed while the file was read. Blame it again.";
    public const string Binary = "This file is binary; it has no lines to blame.";
    public static string Version(string? revision) => revision is null ? "in the working tree" : "at " + GitObjectId.Short(revision);

    public static string Summary(BlameCommit commit) => commit.IsUncommitted ? NotCommitted : HistoryText.Caption(GitObjectId.Short(commit.Id), commit.Summary);

    public static string Label(BlameCommit commit, DateTimeOffset now) =>
        commit.IsUncommitted ? NotCommitted : GitObjectId.Short(commit.Id) + " " + commit.Author + " · " + RelativeTime.Describe(commit.Time, now);
}
