using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain;
using Trag.Domain.Rebase;

namespace Trag.ViewModels.Rebase;

// One commit in the dialog, where the plan puts it: its id and subject, and what happens to it. Its messages are the
// rewrite's draft's (RewriteDraft).
public sealed partial class RewriteRow(RewriteStep step) : ObservableObject
{
    public string Id => step.Id;
    public string ShortId => GitObjectId.Short(step.Id);
    public string Subject => step.Subject;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDropped), nameof(IsFolded))]
    public partial RewriteAction Action { get; set; } = step.Action;
    // A dropped commit is shown dimmed, a squashed or fixed-up one indented under the commit it joins.
    public bool IsDropped => Action == RewriteAction.Drop;
    public bool IsFolded => Action is RewriteAction.Squash or RewriteAction.Fixup;
}
