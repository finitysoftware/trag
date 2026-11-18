using Trag.Domain.Presentation.Text;
using Trag.Domain.Tags;

namespace Trag.ViewModels.Tags;

// One tag as the list shows it; the commands that act on a row live on the view model and take the row.
public sealed record TagRow(TagInfo Tag)
{
    public string Title => Tag.Name;
    public string Detail => TagText.RowDetail(Tag.Commit, Tag.Subject);
}
