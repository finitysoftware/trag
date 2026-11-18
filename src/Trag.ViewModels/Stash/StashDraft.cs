using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Naming;
using Trag.Domain.Stash;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.Stash;

// The stash dialog's input: the name the changes are set aside under and whether untracked files go along; Domain says
// whether the name will do (StashMessages).
public sealed partial class StashDraft : Form
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))] public partial string Message { get; set; } = "";
    [ObservableProperty]
    public partial bool IncludeUntracked { get; set; }
    public bool IsValid => StashMessages.IsValid(Message);
    public StashOperation ToOperation() => new(Message.Trim(), IncludeUntracked);

    protected override void ResetFields()
    {
        Message = "";
        IncludeUntracked = false;
    }
}
