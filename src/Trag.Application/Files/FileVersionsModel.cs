using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain;
using Trag.Domain.Diff;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.Application.Files;

// A commit's version of a file: saved where the user chose, with the status bar saying where or that it could not be,
// or restored into the working tree through the repository's writer, which also undoes what a commit did to the file.
// Restoring over the file's own changes loses them, so that restore is the one asked about first.
public sealed class FileVersionsModel(Repository repository, IHistoryWorkspace? history, StatusBarState statusBar)
{
    public Repository Repository => repository;

    // The name a saved version is offered under: the file's own.
    public string FileName(GitPath path) => Path.GetFileName(path.Display);

    public async Task SaveAsync(string commit, GitPath path, string destination)
    {
        if (repository.Snapshot is not { } snapshot || history is null) return;
        try
        {
            await history.SaveFileAsync(snapshot, commit, path, destination, CancellationToken.None);
            statusBar.Notice = FileVersionText.Saved(path.Display, GitObjectId.Short(commit), destination);
        }
        catch (Exception) { statusBar.Notice = FileVersionText.NotSaved(path.Display); }
    }

    // Restoring a commit's version: at once, unless the file's own changes would go; true when the user has to confirm,
    // and the confirmed restore is RestoreAsync.
    public async Task<bool> RequestRestoreAsync(string commit, GitPath path)
    {
        if (repository.Snapshot?.Files.Any(file => file.IsUnstaged && file.Path.IsSame(path)) == true) return true;
        await RestoreAsync(commit, path);
        return false;
    }

    public Task<GitWriteResult?> RestoreAsync(string commit, GitPath path) =>
        repository.Writer.RunAsync(expected => new RestoreFileRequest(expected, commit, path));

    public Task<GitWriteResult?> RevertAsync(string commit, FileDiffMetadata file) =>
        repository.Writer.RunAsync(expected => new RevertFileRequest(expected, commit, file.Path, file.OriginalPath));
}
