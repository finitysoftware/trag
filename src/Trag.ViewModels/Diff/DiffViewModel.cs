using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Diff;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;

namespace Trag.ViewModels.Diff;

// The diff pane: the document the reader shows and whether a read runs that someone waits for, and the command that
// reads a diff too large to show whole as its bounded preview. The reader reads and keeps the document; the owner hands
// it the source.
public sealed partial class DiffViewModel : ObservableObject
{
    private readonly DiffReader reader;

    public DiffViewModel(DiffReader reader)
    {
        this.reader = reader;
        Document = reader.Document;
        reader.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DiffReader.Document)) Document = reader.Document;
            else if (e.PropertyName == nameof(DiffReader.IsBusy)) OnPropertyChanged(nameof(IsBusy));
        };
    }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(LoadPreviewCommand))]
    public partial DiffDocument Document { get; set; }
    public bool IsBusy => reader.IsBusy;
    public Task Completion => reader.Completion;
    public bool IsCurrent => reader.IsCurrent;

    public void Start(DiffSource source, Func<bool, CancellationToken, Task<FileDiff?>> read) => reader.Start(source, read);

    public void Refresh(DiffSource source, Func<bool, CancellationToken, Task<FileDiff?>> read) => reader.Refresh(source, read);

    private bool CanLoadPreview() => Document.CanPreview;
    [RelayCommand(CanExecute = nameof(CanLoadPreview), AllowConcurrentExecutions = true)]
    private Task LoadPreviewAsync() => reader.LoadPreviewAsync();

    public void Cancel() => reader.Cancel();

    public void Clear() => reader.Clear();
}
