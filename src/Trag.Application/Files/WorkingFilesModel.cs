using Trag.Application.Live;
using Trag.Domain.Naming;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.Application.Files;

// The working tree's files as the machine has them: a file's full path under the shown repository, what the status
// bar says when the file asked for is not there, and an untracked file ignored by a line of .gitignore, through the
// repository's writer.
public sealed class WorkingFilesModel(Repository repository, StatusBarState statusBar)
{
    // A name that is not its own text has no full path to give the shell.
    public string? PathOf(GitPath path) => repository.Snapshot is { } snapshot && path.IsDisplayable ? WorkingFile.PathIn(snapshot.Root, path) : null;

    public void SayMissing(GitPath path) => statusBar.Notice = StatusText.NotInWorkingTree(path.Display);

    public Task<GitWriteResult?> IgnoreAsync(IgnoreChoice choice) =>
        repository.Writer.RunAsync(expected => new IgnoreRequest(expected, choice.Path, choice.Pattern));
}
