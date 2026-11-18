using Trag.Domain.Paths;

namespace Trag.Domain.Blame;

// A commit as blame names it for the lines it last changed: who made it and when, its subject, and the path the file had
// in it. Previous is the commit before it, with the file's path there, which a blame further back starts from; the first
// commit of the file's history has none.
public sealed record BlameCommit(string Id, string Author, DateTimeOffset Time, string Summary, GitPath Path)
{
    public string? Previous { get; init; }
    public GitPath? PreviousPath { get; init; }
    // Lines changed in the working tree and not committed carry Git's all-zero id.
    public bool IsUncommitted => GitObjectId.IsZero(Id);
}
