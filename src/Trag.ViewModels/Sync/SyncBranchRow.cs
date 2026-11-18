using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Sync;

namespace Trag.ViewModels.Sync;

// One upstream choice, including a branch that Push would create. A class, not a record: the selection holds the row by
// identity, and a new read's equal-looking row is still a new choice. The selection that holds it marks the chosen one.
public sealed partial class SyncBranchRow(SyncBranch branch) : ObservableObject
{
    public SyncBranch Branch { get; } = branch;
    public string Label => SyncText.BranchLabel(Branch);
    [ObservableProperty] public partial bool IsChosen { get; internal set; }
}
