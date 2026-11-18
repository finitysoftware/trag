using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Trag.Desktop.Controls.Diff;
using Trag.Desktop.Views;
using Trag.Desktop.Views.History;
using Trag.Desktop.Views.Merge;
using Trag.Desktop.Views.Rebase;

namespace Trag.Desktop.Diagnostics.Probe;

// Every control the probe reaches by name, looked up through the visual tree from the window or a view; the names
// listed here must exist in the XAML (a test checks). Lazily created views are found by type.
internal sealed class ProbeSurface(MainWindow window)
{
    public static readonly string[] Names =
    [
        "Rows", "DiffModeSelector", "SideHeaders", "FetchButton", "PullButton", "PushButton", "CommitMessageEditor",
        "StageButton", "UnstageButton", "CommitButton", "StagedList", "UnstagedList", "HistorySearchEditor", "ConflictList",
        "MergeMessageEditor", "ConflictEditorButton", "MergeToolButton", "StageResolutionButton", "FinishMergeButton",
        "RequestAbortMergeButton", "ConfirmAbortMergeButton", "KeepMergeButton"
    ];

    // The scope's own name scope answers even before the control is laid out (a hidden diff has its rows); anything a
    // sub-view declares is found through the visual tree once it is realized.
    public static T Find<T>(Visual scope, string name) where T : Control => TryFind<T>(scope, name) ?? throw new InvalidOperationException("The probe found no control named " + name + ".");
    public static T? TryFind<T>(Visual scope, string name) where T : Control =>
        (scope as Control)?.FindControl<T>(name) ?? scope.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);
    public T Find<T>(string name) where T : Control => Find<T>(window, name);

    // The diff the user sees; with none visible, the file status diff, reached through its view so it answers before it is laid out.
    public DiffView VisibleDiffView => window.GetVisualDescendants().OfType<DiffView>().FirstOrDefault(view => view.IsEffectivelyVisible) ?? window.Workspace.Diff.FileDiffView;
    public MergeView MergeView => window.GetVisualDescendants().OfType<MergeView>().Single();
    // The view the conflicts show in: the merge view or the rebase view, whichever is open.
    public Control OperationView => window.GetVisualDescendants().OfType<Control>().First(view => view is Views.Merge.MergeView or RebaseView);
    public HistoryView HistoryView => window.GetVisualDescendants().OfType<HistoryView>().Single();
}
