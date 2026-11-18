using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Application.Merge;
using Trag.Domain.Merge;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.ViewModels.Blocks;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.Merge;

// The conflicts of a merge or a rebase stop, as both views show them: the files still in conflict and the files already
// decided, the one selected in either list, and the commands that resolve it or bring a conflict back. The files, the
// sides' names, what each write needs and the writes are the repository's conflicts; every sentence comes from Domain
// (MergeText). The selected conflict is the one whose blocks the conflicts read.
public sealed partial class ResolutionViewModel : ObservableObject
{
    private readonly ConflictsModel conflicts;
    private readonly Repository repository;
    private readonly StatusSelection files;
    private readonly Navigator navigator;

    public ResolutionViewModel(ConflictsModel conflicts, StatusSelection files, Navigator navigator)
    {
        this.conflicts = conflicts;
        repository = conflicts.Repository;
        this.files = files;
        this.navigator = navigator;
        Reopen = new(repository, ReopenAsync, row => MergeText.ReopenQuestion(row.Resolved), Failures.ConflictNotReopened, () => repository.CanResolve);
        repository.SnapshotApplied += (_, _) => Follow();
        repository.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Repository.IsWriting)) FollowWriting(); };
        repository.Requery.Track(StageResolutionCommand, OpenConflictEditorCommand, RunMergeToolCommand, UseYoursCommand, UseOtherCommand, RequestReopenCommand);
    }

    // Entering the view shows the selected file's diff; it may have been chosen while another section was shown.
    public void FollowSection()
    {
        if (IsShown) ShowSelection();
    }

    // A write that ends while the view shows brings the diff back to the file it left selected; a sync write is not the view's.
    private void FollowWriting()
    {
        var writer = repository.Writer;
        if (repository.IsWriting || !writer.WriteBelongsToSession || !IsShown || writer.RunsSync) return;
        ShowSelection();
        files.ShowAgain();
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Sides), nameof(DetailLine))] public partial ConflictScope Scope { get; private set; } = ConflictScope.None;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMarkers), nameof(MarkersText))] public partial MergeFiles Files { get; private set; } = MergeFiles.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasConflicts))] public partial IReadOnlyList<ConflictRow> Conflicts { get; private set; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MarkedRow))] public partial IReadOnlyList<ResolvedRow> Resolved { get; private set; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelectedConflict), nameof(HasSelection), nameof(DetailTitle), nameof(DetailLine), nameof(ShowsMarkResolvedReason))]
    [NotifyCanExecuteChangedFor(nameof(StageResolutionCommand), nameof(OpenConflictEditorCommand), nameof(RunMergeToolCommand), nameof(UseYoursCommand), nameof(UseOtherCommand))]
    public partial ConflictRow? SelectedConflict { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelectedResolved), nameof(HasSelection), nameof(ShowsDiff), nameof(DetailTitle), nameof(DetailLine))]
    [NotifyCanExecuteChangedFor(nameof(RequestReopenCommand))]
    public partial ResolvedRow? SelectedResolved { get; set; }
    // The question the conflicts ask before a file the user edited by hand comes back as a conflict.
    public Confirmation<ResolvedRow> Reopen { get; }

    public ConflictSides Sides => Scope.Sides;
    public bool HasConflicts => Conflicts.Count != 0;
    // A file staged with markers keeps Finish or Continue back; the notice over them names it.
    public bool HasMarkers => Files.HasMarkers;
    public string MarkersText => MergeText.MarkersText(Files.WithMarkers);
    public bool HasSelectedConflict => SelectedConflict is not null;
    public bool HasSelectedResolved => SelectedResolved is not null;
    public bool HasSelection => HasSelectedConflict || HasSelectedResolved;
    // A decided file that is staged has a change to show; a conflict shows its blocks, a deletion nothing.
    public bool ShowsDiff => SelectedResolved?.Resolved.Staged is not null;
    public string DetailTitle => SelectedConflict?.PathText ?? SelectedResolved?.PathText ?? "";
    public string DetailLine => SelectedConflict is { } conflict ? MergeText.ConflictLine(conflict.Conflict, Sides)
        : SelectedResolved is { } resolved ? MergeText.ResolvedLine(resolved.Resolved, Sides) : "";
    public bool ShowsMarkResolvedReason => SelectedConflict is { CanMarkResolved: false };
    // The first file staged with markers (Domain's ConflictFocus), which the notice over Finish or Continue shows or brings back.
    public ResolvedRow? MarkedRow => RowOf(ConflictFocus.WithMarkers(Files));
    private bool IsShown => WorkspaceSections.ShowsDecidedDiff(navigator.Section);

    private bool CanResolve() => conflicts.CanResolve && SelectedConflict is { Conflict.TakesSides: true };
    private bool CanRunMergeTool() => CanResolve() && SelectedConflict!.Conflict.TakesMergeTool;
    private bool CanMarkResolved() => SelectedConflict is { } row && conflicts.CanStage(row.Conflict);
    private bool CanReopen(ResolvedRow? row) => (row ?? SelectedResolved) is { } file && conflicts.CanReopen(file.Resolved);

    partial void OnSelectedConflictChanged(ConflictRow? value)
    {
        if (value is not null) SelectedResolved = null;
        ShowSelection();
        conflicts.Show(value?.Conflict);
    }

    partial void OnSelectedResolvedChanged(ResolvedRow? value)
    {
        if (value is not null) SelectedConflict = null;
        ShowSelection();
    }

    // The diff pane shows what the change does to a decided file; a conflict shows its blocks instead of a diff.
    private void ShowSelection()
    {
        if (IsShown) files.SelectedStatus = SelectedResolved?.Resolved.Staged is { } staged ? new StatusRow(staged, true) : null;
    }

    // Execute/ExecuteAsync do not check CanExecute; the conflicts check what each write needs again, so direct calls
    // (tests, shortcuts, other code) are refused there too.
    [RelayCommand(CanExecute = nameof(CanMarkResolved))]
    private Task StageResolutionAsync() => OnSelected(row => conflicts.StageAsync(row.Conflict));
    [RelayCommand(CanExecute = nameof(CanResolve))]
    private Task UseYoursAsync() => OnSelected(row => conflicts.UseAsync(row.File.Path, conflicts.Sides.Yours));
    [RelayCommand(CanExecute = nameof(CanResolve))]
    private Task UseOtherAsync() => OnSelected(row => conflicts.UseAsync(row.File.Path, conflicts.Sides.Other));
    [RelayCommand(CanExecute = nameof(CanResolve))]
    private Task OpenConflictEditorAsync() => OnSelected(row => conflicts.OpenToolAsync(row.File.Path, editor: true));
    [RelayCommand(CanExecute = nameof(CanRunMergeTool))]
    private Task RunMergeToolAsync() => OnSelected(row => conflicts.OpenToolAsync(row.File.Path, editor: false));

    private Task OnSelected(Func<ConflictRow, Task> write) => SelectedConflict is { } row ? write(row) : Task.CompletedTask;

    // The selected decided file, or the one the markers notice names: the conflicts bring it back, or say that the user
    // has to confirm; then the question opens.
    [RelayCommand(CanExecute = nameof(CanReopen))]
    private async Task RequestReopenAsync(ResolvedRow? row)
    {
        row ??= SelectedResolved;
        if (row is null) return;
        if (await conflicts.RequestReopenAsync(row.Resolved)) Reopen.Request(row);
    }

    private Task<GitWriteResult?> ReopenAsync(ResolvedRow row) => conflicts.ReopenAsync(row.Resolved);

    // The file status sends a conflict here, or the whole view where conflicts are resolved.
    [RelayCommand]
    private void ShowConflict(ConflictRow? row)
    {
        if (row is not null) SelectedConflict = Conflicts.FirstOrDefault(conflict => conflict.File.Path.IsSame(row.File.Path));
        if (Scope.Section is { } section) navigator.Section = section;
    }

    [RelayCommand] private void ShowResolved(ResolvedRow row) => SelectedResolved = row;

    // New lists: the selection follows Domain's rule (ConflictFocus) — kept by path in either list, a conflict that was
    // resolved gives way to the next one, and with nothing selected a file staged with markers is what Finish or Continue
    // waits for, so it is what the view shows.
    private void Follow()
    {
        var (conflict, resolved) = (SelectedConflict?.File.Path, SelectedResolved?.Resolved.Path);
        Files = conflicts.Files;
        Scope = conflicts.Scope;
        Conflicts = Files.Conflicts.Select(file => new ConflictRow(file)).ToArray();
        Resolved = Files.Resolved.Select(file => new ResolvedRow(file, Sides)).ToArray();
        var (nextConflict, nextResolved) = ConflictFocus.Select(Files, conflict, resolved);
        SelectedConflict = Conflicts.FirstOrDefault(row => ReferenceEquals(row.Conflict, nextConflict));
        SelectedResolved = RowOf(nextResolved);
        if (Reopen.Subject is { } asked && Resolved.All(row => !row.Resolved.Path.IsSame(asked.Resolved.Path))) Reopen.Close();
    }

    private ResolvedRow? RowOf(MergeResolved? file) => Resolved.FirstOrDefault(row => ReferenceEquals(row.Resolved, file));
}
