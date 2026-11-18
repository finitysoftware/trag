using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Live;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;

namespace Trag.Application.Diff;

// Reads the diff one pane shows and owns it; the owner supplies only the source and its file read. One Loader reads the
// document, and every document of one read names its source, the messages included, so the view keeps its place. A
// refresh of the shown source reads quietly: the shown diff stays until the new one differs. A diff too large to show
// whole can be read again as its bounded preview.
public sealed partial class DiffReader : ObservableObject
{
    private readonly IDiffPresentationBuilder builder;
    private readonly DiffMessages messages;
    private readonly Loader<DiffDocument> document;
    private (DiffSource Source, Func<bool, CancellationToken, Task<FileDiff?>> Read)? source;
    // The diff on screen and whether it is the bounded preview; a refresh compares against it and reads the same way.
    private (FileDiff Diff, bool Preview)? shown;
    // The last read is a refresh: whatever it ends with, an interruption included, the shown diff stays.
    private bool quiet;

    public DiffReader(IDiffPresentationBuilder builder, CancelRegistry cancels, DiffMessages messages)
    {
        this.builder = builder;
        this.messages = messages;
        Document = DiffDocument.Empty(messages.Empty);
        document = new(Document, cancels) { ChangedMessage = messages.Changed, FailedMessage = messages.Failed, CanceledMessage = messages.Cancelled };
        // The read's value is the document; its message is an empty document of the same source that says why.
        document.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(document.Value)) Document = document.Value;
            else if (e.PropertyName == nameof(document.Message) && document.Message.Length != 0 && !quiet) Document = DiffDocument.Empty(document.Message, source?.Source);
            else if (e.PropertyName == nameof(document.IsBusy)) OnPropertyChanged(nameof(IsBusy));
        };
    }

    [ObservableProperty] public partial DiffDocument Document { get; private set; }
    // A quiet refresh shows no progress: nothing on screen is waiting for it.
    public bool IsBusy => document.IsBusy && !quiet;
    public Task Completion => document.Completion;
    // Whether the diff on screen answers its source as the repository is now: no read of it runs, and the last one did not
    // find the repository moved under it.
    public bool IsCurrent => !document.IsBusy && document.Message != messages.Changed;

    // The read replaces the document at once, so the view never sees a document without a source in between.
    public void Start(DiffSource source, Func<bool, CancellationToken, Task<FileDiff?>> read)
    {
        this.source = (source, read);
        Read(preview: false);
    }

    // The shown source read again after a change on disk: the diff on screen stays while the read runs, stays if the new
    // read shows the same, and is kept too when that read fails, or when the repository moved under it, which the update
    // that moved it sees in IsCurrent and reads again. A refresh replaces a refresh still running; anything that is not
    // the shown diff of this source is a fresh start.
    public void Refresh(DiffSource source, Func<bool, CancellationToken, Task<FileDiff?>> read)
    {
        if (this.source?.Source != source || shown is not { } before || document.IsBusy && !quiet)
        {
            Start(source, read);
            return;
        }
        this.source = (source, read);
        var current = Document;
        quiet = true;
        OnPropertyChanged(nameof(IsBusy));
        _ = document.LoadAsync(async ct =>
        {
            FileDiff? diff;
            try { diff = await read(before.Preview, ct); }
            catch (Exception error) when (error is not OperationCanceledException) { return current; }
            ct.ThrowIfCancellationRequested();
            if (diff is null) return null;
            if (diff.HasSameContent(before.Diff)) return current;
            return await BuildAsync(diff, before.Preview, source, ct);
        });
    }

    // The shown source read again as its bounded preview.
    public Task LoadPreviewAsync() => Read(preview: true);

    private Task Read(bool preview)
    {
        if (source is not { } selected) return Completion;
        var loading = DiffDocument.Empty(messages.Loading, selected.Source);
        Document = loading;
        shown = null;
        quiet = false;
        return document.LoadAsync(async ct =>
        {
            var diff = await selected.Read(preview, ct);
            ct.ThrowIfCancellationRequested();
            if (diff is null) return null;
            return await BuildAsync(diff, preview, selected.Source, ct);
        }, interim: loading);
    }

    private async Task<DiffDocument> BuildAsync(FileDiff diff, bool preview, DiffSource source, CancellationToken ct)
    {
        var presentation = await builder.BuildAsync(diff, source.Path, ct);
        ct.ThrowIfCancellationRequested();
        shown = (diff, preview);
        return DiffDocument.From(diff, presentation with { Source = source });
    }

    public void Cancel() => document.Cancel();

    public void Clear()
    {
        source = null;
        shown = null;
        document.Clear();
    }
}
