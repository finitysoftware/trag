using Trag.Domain.Presentation.Text;
using Trag.Domain.Stash;

namespace Trag.ViewModels.Stash;

// One entry as the picker shows it; the commands that act on a row live on the view model and take the row.
public sealed record StashRow(StashEntry Entry, TimeProvider Clock)
{
    public string Id => Entry.Id;
    public string Title => Entry.Title;
    public string Detail => StashText.Detail(Entry, Clock.GetUtcNow());
    public string Tooltip => StashText.Tooltip(Entry);
}
