using Trag.Domain.Presentation.Text;

namespace Trag.ViewModels.Branches;

// One remote's branches in the picker, under the remote's title.
public sealed record BranchGroup(string Remote, IReadOnlyList<BranchRow> Rows)
{
    public string Title => BranchText.GroupTitle(Remote);
}
