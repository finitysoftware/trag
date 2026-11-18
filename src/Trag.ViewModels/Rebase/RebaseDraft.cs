using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Rebase;
using Trag.ViewModels.Blocks;
using Trag.ViewModels.Branches;

namespace Trag.ViewModels.Rebase;

// The rebase side of the merge dialog: the current branch and the branch to replay it onto, as rows of the picker's list.
// Every rule about what the input may be is the Domain choice's; this only holds it.
public sealed partial class RebaseDraft : Form
{
    private string? wantedOnto;

    [ObservableProperty] public partial IReadOnlyList<BranchRow> OntoOptions { get; private set; } = [];
    [ObservableProperty] public partial BranchRow? Onto { get; set; }
    [ObservableProperty] public partial BranchRow? Branch { get; private set; }

    public RebaseChoice Choice => new(Onto?.Branch, Branch?.Branch);

    // The branch to rebase onto may already be chosen, by the merge side of the dialog.
    public void Open(string? ontoRef)
    {
        Reset();
        wantedOnto = ontoRef;
        IsOpen = true;
    }

    // The other way round: once the checked-out branch changes, the one that was checked out becomes the base.
    public void Retarget(string ontoRef)
    {
        wantedOnto = ontoRef;
        Onto = null;
    }

    // A new read of the branches: the current one is rebased, the bases are Domain's (RebaseOptions), kept by ref.
    public void Update(BranchList list)
    {
        var onto = Onto?.Branch.Ref ?? wantedOnto;
        Branch = list.Current;
        OntoOptions = RebaseOptions.Onto(list.Catalog).Select(list.Row).ToArray();
        Onto = OntoOptions.FirstOrDefault(row => row.Branch.Ref == onto);
    }

    protected override void ResetFields()
    {
        OntoOptions = [];
        Onto = Branch = null;
        wantedOnto = null;
    }
}
