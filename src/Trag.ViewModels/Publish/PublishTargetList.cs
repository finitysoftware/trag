using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Branches;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;

namespace Trag.ViewModels.Publish;

// The targets Domain computes for one branch read, one remote and the current branch (PublishTargets), held as selectable
// options with their texts; changing the remote only rebuilds that remote's options.
public sealed partial class PublishTargetList : ObservableObject
{
    private BranchCatalog source = BranchCatalog.Empty;
    private IReadOnlyDictionary<string, string[]> namesByRemote = PublishTargets.Index([]);
    private string? remote;
    private string? branch;

    [ObservableProperty]
    public partial IReadOnlyList<PublishTargetOption> Items { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning), nameof(HasWarning), nameof(Summary))]
    public partial PublishTargetOption? Selected { get; set; }

    public string Warning => PublishText.TargetWarning(Selected?.Target, branch);
    public bool HasWarning => Warning.Length != 0;
    public string Summary => PublishText.TargetSummary(branch, remote, Selected?.Name);

    public void Update(BranchCatalog branches, string? remoteName, string? currentBranch)
    {
        var branchesChanged = !ReferenceEquals(source, branches);
        if (!branchesChanged && remote == remoteName && branch == currentBranch)
            return;
        if (branchesChanged)
        {
            source = branches;
            namesByRemote = PublishTargets.Index(branches.Remote);
        }

        remote = remoteName;
        branch = currentBranch;
        var previous = Selected?.Name;
        Items = PublishTargets.For(namesByRemote, remote, branch).Select(target => new PublishTargetOption(target)).ToArray();
        Select(previous);
        // A new local branch can change the text even when the selected option is equal.
        OnPropertyChanged(nameof(Warning));
        OnPropertyChanged(nameof(HasWarning));
        OnPropertyChanged(nameof(Summary));
    }

    public void Select(string? name) => Selected = Items.FirstOrDefault(option => option.Name == name) ?? Items.FirstOrDefault();
}
