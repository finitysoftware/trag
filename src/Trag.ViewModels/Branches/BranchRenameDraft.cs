using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Branches;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.Branches;

// The rename dialog: the branch, and its new name checked against the list the way a new branch's is. The name it has
// is no new name, so it is not offered, though it says nothing wrong either.
public sealed partial class BranchRenameDraft : Form
{
    private BranchCatalog branches = BranchCatalog.Empty;

    public BranchRow? Branch { get; private set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NameError), nameof(HasNameError), nameof(IsValid))]
    public partial string Name { get; set; } = "";
    private bool IsUnchanged => Name.Trim() == Branch?.Name;
    public string NameError => Branch is not { } branch || IsUnchanged ? "" : BranchNames.RenameError(Name, branch.Name, branches.LocalNames);
    public bool HasNameError => NameError.Length != 0;
    public bool IsValid => Branch is { } branch && !IsUnchanged && BranchNames.IsValidRename(Name, branch.Name, branches.LocalNames);
    public string Subtitle => BranchText.RenameSubtitle(Branch?.Name ?? "");

    public void Open(BranchRow branch, BranchCatalog branches)
    {
        Reset();
        (Branch, this.branches) = (branch, branches);
        Name = branch.Name;
        OnPropertyChanged(nameof(Branch));
        OnPropertyChanged(nameof(Subtitle));
        IsOpen = true;
    }

    // A new list: the new name is checked against the branches it holds.
    public void Update(BranchCatalog branches)
    {
        this.branches = branches;
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(HasNameError));
        OnPropertyChanged(nameof(IsValid));
    }

    protected override void ResetFields()
    {
        Name = "";
        Branch = null;
    }
}
