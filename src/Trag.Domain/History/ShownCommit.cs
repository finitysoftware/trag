using Trag.Domain.Presentation.Text;

namespace Trag.Domain.History;

// What there is for the commit the commit pane shows: its details once they are read, or the line that stands in for them.
public sealed record ShownCommit(CommitDetails? Details, string Heading)
{
    public static readonly ShownCommit None = new(null, CommitText.SelectCommit);
    public static readonly ShownCommit Loading = new(null, CommitText.Loading);

    public static ShownCommit Of(CommitDetails details) => new(details, details.Commit.Id);
}
