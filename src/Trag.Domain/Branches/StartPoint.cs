using Trag.Domain.History;
using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Branches;

// Where a new branch starts: current HEAD (no ref), a commit chosen in History, or another branch.
public sealed record StartPoint(string Display, string? Ref)
{
    public static StartPoint AtCommit(HistoryCommit commit) => new(commit.Caption, commit.Id);

    // HEAD first, then the commit the dialog was opened for, the other local branches and the remote ones.
    public static IReadOnlyList<StartPoint> For(BranchCatalog catalog, string? currentBranch, StartPoint? initial) =>
    [
        new(BranchText.StartAtHead(currentBranch), null),
        .. initial is null ? [] : new[] { initial },
        .. catalog.Local.Where(branch => branch.Name != currentBranch).Select(branch => new StartPoint(branch.Name, branch.Ref)),
        .. catalog.Remote.Select(branch => new StartPoint(branch.Name, branch.Ref))
    ];
}
