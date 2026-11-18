using Trag.Domain.Accounts;
using Trag.Domain.Branches;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;

namespace Trag.ViewModels.Branches;

// One branch as the picker shows it; the commands that act on a row live on the view model and take the row.
public sealed record BranchRow(BranchInfo Branch, bool IsCurrent)
{
    public string Name => Branch.Name;
    public bool IsRemote => Branch.IsRemote;
    // What the row's menu offers: a local branch is renamed and asked a request for; any other than the current one is
    // merged and deleted.
    public bool CanRename => !IsRemote;
    public bool CanRequest => !IsRemote;
    public bool CanDelete => !IsCurrent;
    public bool CanMerge => !IsCurrent;
    // The page that starts the branch's pull or merge request, and the provider that names it: the page's own, or for a
    // branch no page fits, that of the repository's preferred remote.
    public PullRequestPage? Request { get; init; }
    public AccountProvider? RequestProvider { get; init; }
    public string RequestLabel => BranchText.CreateRequest(RequestProvider);
    public string RequestIcon => IconKinds.Account(RequestProvider ?? AccountProvider.GitHub);
    public string MergeTip => BranchText.MergeTip(Branch);
    public string DeleteTitle => BranchText.DeleteTitle(Branch);
    public string DeleteSubtitle => BranchText.DeleteSubtitle(Branch);
    public string DeleteText => BranchText.DeleteText(Branch);
    public string Tooltip => BranchText.Tooltip(Branch);
}
