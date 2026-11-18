using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Branches;
using Trag.Domain.Naming;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.Branches;

// A dialog that asks for one branch name and judges it against the branch list; Git validates again before anything changes.
public sealed partial class BranchNameDraft(Func<BranchCatalog> branches) : Form
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NameError), nameof(HasNameError), nameof(IsValid))]
    public partial string Name { get; set; } = "";
    public string NameError => BranchNames.Error(Name, branches().LocalNames);
    public bool HasNameError => NameError.Length != 0;
    public bool IsValid => BranchNames.IsValid(Name, branches().LocalNames);

    public void Open(string name)
    {
        Reset();
        Name = name;
        IsOpen = true;
    }

    // The list changed under the open dialog: the name is judged again.
    public void Revalidate()
    {
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(HasNameError));
        OnPropertyChanged(nameof(IsValid));
    }

    protected override void ResetFields() => Name = "";
}
