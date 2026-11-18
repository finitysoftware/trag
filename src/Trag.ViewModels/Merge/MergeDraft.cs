using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Merge;
using Trag.Domain.Naming;
using Trag.ViewModels.Blocks;
using Trag.ViewModels.Branches;

namespace Trag.ViewModels.Merge;

// The merge dialog's input: the two branches as rows of the picker's list, so the current one carries its tag, the three
// options and the message, which follows the branches until the user writes one of their own. Every rule about what the
// input may be is the Domain choice's; this only holds it.
public sealed partial class MergeDraft : Form
{
    private string? current;
    private string? wantedFrom;
    private string defaultMessage = "";

    [ObservableProperty] public partial IReadOnlyList<BranchRow> FromOptions { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<BranchRow> IntoOptions { get; private set; } = [];
    [ObservableProperty] public partial BranchRow? From { get; set; }
    [ObservableProperty] public partial BranchRow? Into { get; set; }
    [ObservableProperty] public partial bool NoFastForward { get; set; }
    [ObservableProperty] public partial bool StayOnTarget { get; set; }
    [ObservableProperty] public partial bool PushAfter { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = "";

    public MergeChoice Choice => new(From?.Branch, Into?.Branch, current)
    {
        NoFastForward = NoFastForward, StayOnTarget = StayOnTarget, PushAfter = PushAfter, Message = Message
    };

    // The current branch is the target until the user picks another; the branch to merge from may already be chosen.
    public void Open(string? fromRef)
    {
        Reset();
        wantedFrom = fromRef;
        IsOpen = true;
    }

    // A new read of the branches: the options are Domain's (MergeOptions), and both choices are kept by ref.
    // The branch checked out now is the one a merge into another branch comes back to.
    public void Update(BranchList list, string? currentBranch)
    {
        current = currentBranch;
        var from = From?.Branch.Ref ?? wantedFrom;
        var into = Into?.Branch.Ref ?? list.Current?.Branch.Ref;
        FromOptions = MergeOptions.From(list.Catalog).Select(list.Row).ToArray();
        IntoOptions = MergeOptions.Into(list.Catalog).Select(list.Row).ToArray();
        From = FromOptions.FirstOrDefault(row => row.Branch.Ref == from);
        Into = IntoOptions.FirstOrDefault(row => row.Branch.Ref == into);
    }

    public void Swap()
    {
        if (Choice.CanSwap) (From, Into) = (Into, From);
    }

    partial void OnFromChanged(BranchRow? value) => FollowBranches();
    partial void OnIntoChanged(BranchRow? value) => FollowBranches();

    // The message follows the branches while it is still the one Trag wrote.
    private void FollowBranches()
    {
        var next = MergeMessages.Default(From?.Branch, Into?.Branch);
        Message = MergeMessages.Following(Message, defaultMessage, next);
        defaultMessage = next;
    }

    protected override void ResetFields()
    {
        FromOptions = IntoOptions = [];
        From = Into = null;
        NoFastForward = StayOnTarget = PushAfter = false;
        Message = defaultMessage = "";
        current = wantedFrom = null;
    }
}
