using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Branches;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.Branches;

// The create-branch dialog: a name checked against the list, a start point, and whether to switch afterwards.
public sealed partial class BranchDraft : Form
{
    private BranchCatalog branches = BranchCatalog.Empty;
    private StartPointOption? initialStart;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NameError), nameof(HasNameError), nameof(IsValid))]
    public partial string Name { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<StartPointOption> StartPoints { get; private set; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial StartPointOption? SelectedStartPoint { get; set; }
    [ObservableProperty] public partial bool SwitchAfterCreate { get; set; } = true;
    public string NameError => BranchNames.Error(Name, branches.LocalNames);
    public bool HasNameError => NameError.Length != 0;
    public bool IsValid => BranchNames.IsValid(Name, branches.LocalNames);
    public string Subtitle => BranchText.CreateSubtitle(SelectedStartPoint?.Point);
    public BranchOperation ToOperation() => new(Name.Trim(), SelectedStartPoint?.Ref, SwitchAfterCreate);

    public void Open(BranchCatalog branches, string? currentBranch, StartPointOption? start)
    {
        Reset();
        initialStart = start;
        Update(branches, currentBranch);
        IsOpen = true;
    }

    public void Update(BranchCatalog branches, string? currentBranch)
    {
        this.branches = branches;
        var selected = SelectedStartPoint is null ? initialStart?.Ref : SelectedStartPoint.Ref;
        var options = StartPoint.For(branches, currentBranch, initialStart?.Point)
            .Select(point => ReferenceEquals(point, initialStart?.Point) ? initialStart! : new StartPointOption(point)).ToArray();
        StartPoints = options;
        SelectedStartPoint = options.FirstOrDefault(option => option.Ref == selected) ?? options[0];
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(HasNameError));
        OnPropertyChanged(nameof(IsValid));
    }

    protected override void ResetFields()
    {
        Name = "";
        SwitchAfterCreate = true;
        SelectedStartPoint = null;
        StartPoints = [];
        initialStart = null;
    }
}
