using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.ViewModels.Merge;

// One file still in conflict, as the merge view and the file status list it.
public sealed record ConflictRow(MergeConflict Conflict)
{
    public FileStatus File => Conflict.File;
    public string PathText => File.Path.DisplayEscaped;
    public string BlocksText => MergeText.Blocks(Conflict.Blocks);
    public bool CanMarkResolved => Conflict.CanMarkResolved;
}
