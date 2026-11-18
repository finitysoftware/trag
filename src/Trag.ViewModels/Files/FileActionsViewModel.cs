using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Files;
using Trag.Domain.Naming;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.ViewModels.Blocks;
using Trag.ViewModels.History;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.Files;

// A file's own menu, in the status lists and among a commit's files: the working tree's file opened in its program or
// shown in the file manager, its path copied, a submodule opened as a repository, an untracked one ignored, or a commit's version saved where the window's
// Save dialog says or restored, behind a question when the file's own changes would go, or what a commit did to it
// undone (FileVersionsModel). A file the working tree does not have is said by
// the working files' model; one that would run is not opened (WorkingFile).
public sealed partial class FileActionsViewModel : ObservableObject
{
    private readonly WorkingFilesModel files;
    private readonly FileVersionsModel versions;

    public FileActionsViewModel(WorkingFilesModel files, FileVersionsModel versions)
    {
        this.files = files;
        this.versions = versions;
        Restore = new(versions.Repository, RestoreAsync, row => FileVersionText.RestoreQuestion(row.File.Path.Display, row.Commit),
            FileVersionText.NotRestored);
        versions.Repository.Requery.Track(IgnoreCommand, RequestRestoreCommand, RevertFileCommand, OpenSubmoduleCommand);
    }

    public Confirmation<CommitFileRow> Restore { get; }

    // The window's shell, which opens and copies; the composition links it, and without one nothing happens.
    public Func<IShellHost?> Host { get; set; } = () => null;

    private bool CanReach(GitPath? path) => path is not null && files.PathOf(path) is not null;
    private bool CanOpen(GitPath? path) => CanReach(path) && WorkingFile.Opens(path!);

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open(GitPath? path)
    {
        if (CanOpen(path) && Host()?.OpenFile(files.PathOf(path!)!) == false) files.SayMissing(path!);
    }

    [RelayCommand(CanExecute = nameof(CanReach))]
    private void Reveal(GitPath? path)
    {
        if (Host()?.RevealFile(files.PathOf(path!)!) == false) files.SayMissing(path!);
    }

    [RelayCommand(CanExecute = nameof(CanReach))]
    private void CopyPath(GitPath? path) => Host()?.CopyText(files.PathOf(path!)!);

    // A submodule opens as a repository of its own; the composition links how a repository opens.
    public Func<string, Task> OpenRepository { get; set; } = _ => Task.CompletedTask;
    private bool CanOpenSubmodule(FileStatus? file) => file is { SubmoduleIsCheckedOut: true } && CanReach(file.Path) && !versions.Repository.IsWriting;
    [RelayCommand(CanExecute = nameof(CanOpenSubmodule))]
    private Task OpenSubmoduleAsync(FileStatus? file) => CanOpenSubmodule(file) ? OpenRepository(files.PathOf(file!.Path)!) : Task.CompletedTask;

    private bool CanIgnore(IgnoreChoice? choice) => choice is not null && versions.Repository.CanWrite;
    [RelayCommand(CanExecute = nameof(CanIgnore))]
    private Task IgnoreAsync(IgnoreChoice? choice) => CanIgnore(choice) ? files.IgnoreAsync(choice!) : Task.CompletedTask;

    private static bool HasVersion(CommitFileRow? row) => row is { File.HasVersion: true };

    private bool CanRestore(CommitFileRow? row) => HasVersion(row) && versions.Repository.CanWrite;
    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RequestRestoreAsync(CommitFileRow? row)
    {
        if (CanRestore(row) && await versions.RequestRestoreAsync(row!.Commit, row.File.Path)) Restore.Request(row);
    }

    private Task<GitWriteResult?> RestoreAsync(CommitFileRow row) => versions.RestoreAsync(row.Commit, row.File.Path);

    private bool CanRevertFile(CommitFileRow? row) => row is { File.CanRevert: true } && versions.Repository.CanWrite;
    [RelayCommand(CanExecute = nameof(CanRevertFile))]
    private Task RevertFileAsync(CommitFileRow? row) => CanRevertFile(row) ? versions.RevertAsync(row!.Commit, row.File) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(HasVersion))]
    private async Task SaveVersionAsync(CommitFileRow? row)
    {
        if (!HasVersion(row) || Host() is not { } host) return;
        if (await host.PickSaveFileAsync(versions.FileName(row!.File.Path)) is { } destination) await versions.SaveAsync(row.Commit, row.File.Path, destination);
    }
}
