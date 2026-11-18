using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Application.Merge;
using Trag.Domain.Merge;

namespace Trag.ViewModels.Merge;

// The conflict blocks of the file selected in the merge or rebase view, and the choice made on one block. The repository's
// conflicts read the file and write the choice; this arranges each block's lines yours first, as the cards show them.
public sealed partial class ConflictViewModel : ObservableObject
{
    private readonly ConflictsModel conflicts;

    public ConflictViewModel(ConflictsModel conflicts)
    {
        this.conflicts = conflicts;
        Document.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Document.Value)) return;
            Parts = Document.Value.Parts.Select(Arrange).ToArray();
            conflicts.Repository.Requery.Invalidate();
        };
        conflicts.Repository.Requery.Track(TakeYoursCommand, TakeOtherCommand, TakeBothCommand);
    }

    public Loader<ConflictView> Document => conflicts.Document;
    [ObservableProperty] public partial IReadOnlyList<ConflictPartRow> Parts { get; private set; } = [];

    private ConflictPartRow Arrange(ConflictPart part)
    {
        var (yours, other) = conflicts.Sides.Arrange(part);
        return new(part, yours, other);
    }

    private bool CanTake(ConflictPartRow? row) => row is not null && conflicts.CanTake(row.Part);

    [RelayCommand(CanExecute = nameof(CanTake))] private Task TakeYoursAsync(ConflictPartRow? row) => TakeAsync(row, conflicts.Sides.Yours.Git);
    [RelayCommand(CanExecute = nameof(CanTake))] private Task TakeOtherAsync(ConflictPartRow? row) => TakeAsync(row, conflicts.Sides.Other.Git);
    [RelayCommand(CanExecute = nameof(CanTake))] private Task TakeBothAsync(ConflictPartRow? row) => TakeAsync(row, ConflictChoice.Both);

    private Task TakeAsync(ConflictPartRow? row, ConflictChoice choice) => row is null ? Task.CompletedTask : conflicts.TakeAsync(row.Part, choice);
}
