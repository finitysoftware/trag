using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace Trag.Desktop.Diagnostics.Probe.Commands;

// A merge in progress: its state line, showing its view, the conflict list layout, the merge message, and the actions
// through their buttons, awaited or only started.
internal static class MergeCommands
{
    public static void Register(ProbeCommandLoop loop, ProbeContext p)
    {
        loop.Add("merge-state", () => Dispatcher.UIThread.Post(() =>
            Console.WriteLine($"TRAG_MERGE {ProbeContext.Flag(p.Shell.Repository.HasMerge)} {p.Shell.Resolution.Conflicts.Count} {ProbeContext.Flag(p.Shell.Merge.FinishMergeCommand.CanExecute(null))} {ProbeContext.Flag(p.Shell.Repository.IsWriting)} {ProbeContext.Flag(p.Shell.Merge.Abort.IsOpen)}")));
        loop.Add("merge-view", () => Dispatcher.UIThread.Post(() => { p.Shell.Navigator.ShowMergeCommand.Execute(null); Console.WriteLine("TRAG_MERGE_VIEW"); }));
        loop.Add("merge-layout", () => Dispatcher.UIThread.Post(() =>
        {
            p.Window.UpdateLayout(); var panel = p.Surface.OperationView;
            // The list takes the height of its rows (up to about half the column): one visible row is a laid-out list.
            Console.WriteLine("TRAG_MERGE_LAYOUT " + (ProbeSurface.Find<ListBox>(panel, "ConflictList").Bounds.Height >= 20 ? "1" : "0"));
        }));
        loop.Add("merge-message", rest =>
        {
            var message = ProbeContext.Decode(rest);
            Dispatcher.UIThread.Post(() => { p.Window.UpdateLayout(); ProbeSurface.Find<TextBox>(p.Surface.MergeView, "MergeMessageEditor").Text = message; Console.WriteLine("TRAG_MERGE_MESSAGE_OK"); });
        });
        loop.Add("merge-action", name => Run(p, name, wait: true));
        loop.Add("merge-start", name => Run(p, name, wait: false));
    }

    private static void Run(ProbeContext p, string name, bool wait) => Dispatcher.UIThread.Post(async () =>
    {
        p.Window.UpdateLayout();
        var panel = p.Surface.OperationView;
        var button = ProbeSurface.Find<Button>(panel, name switch { "editor" => "ConflictEditorButton", "tool" => "MergeToolButton", "stage" => "StageResolutionButton",
            "finish" => "FinishMergeButton", "abort" => "RequestAbortMergeButton", "confirm-abort" => "ConfirmAbortMergeButton", _ => "KeepMergeButton" });
        // A command that is not asynchronous is done when it returns, so there is nothing to start or wait for.
        var isAsync = button.Command is IAsyncRelayCommand;
        if (!await ProbeContext.PressAsync(button, wait || !isAsync ? null : () => Console.WriteLine("TRAG_MERGE_STARTED")))
        { Console.WriteLine("TRAG_MERGE_DISABLED"); return; }
        if (!isAsync) { Console.WriteLine("TRAG_MERGE_ACTION_OK"); return; }
        await p.Shell.Sync.SyncCompletion;
        var result = p.Shell.Repository.Writer.LastWrite?.Result;
        Console.WriteLine(wait ? "TRAG_MERGE_RESULT " + result?.Message + (result?.CommitId is { } id ? " " + id : "") : "TRAG_MERGE_FINISHED");
    });
}
