using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;

namespace Trag.ViewModels.History;

// One file of the shown commit, as the commit pane lists it, with the commit it belongs to.
public sealed record CommitFileRow(FileDiffMetadata File, string Commit)
{
    public string PathText => File.Path.DisplayEscaped;
    public string Status => File.Status;
    public string Description => CommitText.FileDescription(File);
}
