using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.ViewModels.Status;

// One file of a status list, on one side; the list builds rows only for what the view shows.
public sealed record StatusRow(FileStatus File, bool FromIndex)
{
    public string PathText => File.Path.DisplayEscaped;
    public string OriginalPathText => StatusText.OriginalPath(File);
    public string Source => StatusText.Source(FromIndex);
    public string Code => StatusText.Code(File, FromIndex);
    public string Description => StatusText.Description(File, FromIndex);
    // A submodule and a repository inside the working tree are folders, which a file's Open and Reveal cannot show.
    public bool IsFile => !File.IsSubmodule && !File.IsRepositoryOfItsOwn;
    // What its menu offers .gitignore when the file is untracked; each is absent when the name cannot be written as one.
    public bool OffersIgnore => IgnoreChoices.Offered(File);
    public IgnoreChoice? IgnoreFile => IgnoreChoices.File(File.Path);
    public IgnoreChoice? IgnoreExtension => IgnoreChoices.Extension(File.Path);
    public IgnoreChoice? IgnoreFolder => IgnoreChoices.Folder(File.Path);
}
