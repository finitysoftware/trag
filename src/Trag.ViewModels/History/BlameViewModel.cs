using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.History;
using Trag.Domain.Blame;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.History;

// The blame view over the main area: a file blamed line by line at a commit or in the working tree, opened from a file's
// menu and closed onto whatever was shown under it, which another section closes too. The commit of a chosen block is
// found in the history, or the file is blamed again from the commit before it, by the name it had there.
public sealed partial class BlameViewModel : ObservableObject
{
    private readonly BlameModel blame;

    public BlameViewModel(BlameModel blame)
    {
        this.blame = blame;
        blame.Blame.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(blame.Blame.Value)) OnPropertyChanged(nameof(Rows));
            else if (e.PropertyName == nameof(blame.Blame.Message)) OnPropertyChanged(nameof(Message));
            else if (e.PropertyName == nameof(blame.Blame.IsBusy)) OnPropertyChanged(nameof(IsBusy));
        };
        blame.Requery.Track(BlameStatusCommand);
    }

    public Func<string, Task> FindCommit { get; set; } = _ => Task.CompletedTask;
    [ObservableProperty] public partial bool IsOpen { get; private set; }
    // However the view closes, the blame stops following the repository.
    partial void OnIsOpenChanged(bool value)
    {
        if (!value) blame.Close();
    }
    [ObservableProperty] public partial string Path { get; private set; } = "";
    [ObservableProperty] public partial string Version { get; private set; } = "";
    public IReadOnlyList<BlameRow> Rows => blame.Blame.Value;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Summary)), NotifyCanExecuteChangedFor(nameof(ShowCommitCommand), nameof(BlameBeforeCommand))]
    public partial BlameRow? SelectedRow { get; set; }
    public string Message => blame.Blame.Message;
    public string Summary => SelectedRow is { } row ? BlameText.Summary(row.Line.Commit) : "";
    public bool IsBusy => blame.Blame.IsBusy;

    // Git's -w and -C, chosen in the header: the shown file is blamed again.
    public bool IgnoreWhitespace
    {
        get => blame.Options.IgnoreWhitespace;
        set => Use(blame.Options with { IgnoreWhitespace = value });
    }

    public bool DetectMoves
    {
        get => blame.Options.DetectMoves;
        set => Use(blame.Options with { DetectMoves = value });
    }

    private void Use(BlameOptions options)
    {
        _ = blame.UseAsync(options);
        OnPropertyChanged(nameof(IgnoreWhitespace));
        OnPropertyChanged(nameof(DetectMoves));
    }

    public Task OpenAsync(string path, string? revision)
    {
        (Path, Version, SelectedRow, IsOpen) = (path, BlameText.Version(revision), null, true);
        return blame.OpenAsync(path, revision);
    }

    // From a file's menu: a status file in the working tree, or a commit's file at that commit.
    private bool CanBlameStatus(StatusRow? row) => row is { File.CanBlame: true } && blame.CanBlameWorkingTree;
    [RelayCommand(CanExecute = nameof(CanBlameStatus))]
    private Task BlameStatusAsync(StatusRow? row) => OpenAsync(row!.File.Path.Display, null);

    private static bool CanBlameCommitFile(CommitFileRow? row) => row is { File.CanBlame: true };
    [RelayCommand(CanExecute = nameof(CanBlameCommitFile))]
    private Task BlameCommitFileAsync(CommitFileRow? row) => OpenAsync(row!.File.Path.Display, row.Commit);

    public void FollowSection() => IsOpen = false;

    [RelayCommand] private void Close() => IsOpen = false;

    private bool CanShowCommit() => SelectedRow is { Line.Commit.IsUncommitted: false };
    [RelayCommand(CanExecute = nameof(CanShowCommit))]
    private Task ShowCommitAsync()
    {
        IsOpen = false;
        return FindCommit(SelectedRow!.Line.Commit.Id);
    }

    private bool CanBlameBefore() => SelectedRow?.Line.Commit is { Previous: not null };
    [RelayCommand(CanExecute = nameof(CanBlameBefore))]
    private Task BlameBeforeAsync()
    {
        var commit = SelectedRow!.Line.Commit;
        return OpenAsync(commit.PreviousPath!.Display, commit.Previous);
    }
}
