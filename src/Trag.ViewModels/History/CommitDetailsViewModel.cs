using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Diff;
using Trag.Application.History;
using Trag.Domain.History;
using Trag.Domain.Presentation;
using Trag.ViewModels.Diff;

namespace Trag.ViewModels.History;

// History and stash previews share the same commit pane: the shown commit with its files as rows, the file chosen among
// them, and that file's diff. Which commit is shown and every read are the repository's (CommitDetailsModel).
public sealed partial class CommitDetailsViewModel : ObservableObject
{
    private readonly CommitDetailsModel details;
    public DiffViewModel Diff { get; }

    public CommitDetailsViewModel(CommitDetailsModel details, DiffReader reader)
    {
        this.details = details;
        Diff = new(reader);
        var commit = details.Commit;
        // The pane shows the commit, or the reason there is none to show.
        commit.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(commit.Value)) Show(commit.Value);
            else if (e.PropertyName == nameof(commit.Message) && commit.Message.Length != 0) Panel = CommitPanel.Empty(commit.Message);
            else if (e.PropertyName == nameof(commit.IsBusy)) OnPropertyChanged(nameof(IsBusy));
        };
        // Another commit, or none: the pane starts over.
        details.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CommitDetailsModel.Subject)) return;
            Panel = CommitPanel.Empty();
            Diff.Clear();
            HasSubject = details.Subject is not null;
        };
        details.Interrupted += Diff.Cancel;
    }

    public bool IsBusy => details.Commit.IsBusy;
    public Task Completion => details.Commit.Completion;
    // A commit or stash preview was asked for, whether it is still loading or could not be read.
    [ObservableProperty] public partial bool HasSubject { get; private set; }
    [ObservableProperty] public partial CommitPanel Panel { get; set; } = CommitPanel.Empty();
    partial void OnPanelChanged(CommitPanel oldValue, CommitPanel newValue)
    {
        oldValue.SelectionChanged -= SelectFile;
        newValue.SelectionChanged += SelectFile;
    }

    public Task Load(HistoryCommit? selected) => details.Show(selected);

    // In a file's history the commit opens on that file, by the path it has in the commit.
    private void Show(ShownCommit shown)
    {
        if (shown.Details is not { } read)
        {
            Panel = CommitPanel.Empty(shown.Heading);
            return;
        }
        Panel = CommitPanel.From(read);
        Panel.SelectedFile = Panel.Files.FirstOrDefault(row => row.File.Path.IsSame(details.Subject?.Path));
    }

    private void SelectFile(CommitFileRow? file)
    {
        if (file is null || Panel.Details is not { } shown || details.DiffOf(shown, file.File) is not { } read)
        {
            Diff.Clear();
            return;
        }
        Diff.Start(new DiffSource(file.PathText, shown.Commit.Id), read);
    }

    public void Clear() => details.Clear();
}
