using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
namespace Trag.Domain.History;

public sealed record HistoryCommit(string Id, string Author, string Email, string Date, string Subject)
{
    // Parent IDs drive the commit graph; an empty list marks a root commit.
    public IReadOnlyList<string> Parents { get; init; } = [];
    // The branches, tags and detached HEAD on this commit, HEAD's own name first.
    public IReadOnlyList<CommitRef> Refs { get; init; } = [];
    // In a file's history: the path the file has in this commit, and the one it came from when this commit renamed or
    // copied it.
    public GitPath? Path { get; init; }
    public GitPath? OriginalPath { get; init; }
    public string OriginalPathText => OriginalPath is { } path ? HistoryText.CameFrom(path.Display) : "";
    public string ShortId => GitObjectId.Short(Id, 10);
    public string DateText => HistoryText.Date(Date);
    // The commit as a menu names it.
    public string Caption => HistoryText.Caption(ShortId, Subject);
}
