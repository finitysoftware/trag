using Avalonia.Controls;
using Avalonia.Threading;

namespace Trag.Desktop.Diagnostics.Probe.Commands;

// Sync: the state and count lines, the push target, choosing the remote and the upstream branch as the sidebar pickers
// choose them, and fetch/pull/push through the toolbar buttons, awaited or only started.
internal static class SyncCommands
{
    public static void Register(ProbeCommandLoop loop, ProbeContext p)
    {
        loop.Add("sync-state", () => Dispatcher.UIThread.Post(async () =>
        {
            await p.Shell.Sync.SyncCompletion;
            ReportState(p);
        }));
        loop.Add("sync-poll", () => Dispatcher.UIThread.Post(() => ReportState(p)));
        foreach (var command in new[] { "sync-counts", "sync-target", "sync-result" })
            loop.Add(command, () => Dispatcher.UIThread.Post(() => Console.WriteLine(command switch
            {
                "sync-counts" => "TRAG_SYNC_COUNTS " + p.Shell.Sync.Panel.Counts.Text,
                "sync-target" => "TRAG_SYNC_TARGET " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(p.Shell.Sync.Panel.Target.PushDestination)),
                _ => "TRAG_SYNC_RESULT " + p.Shell.StatusBar.SyncOperationText
            })));
        loop.Add("sync-remote", value => Select(p, ProbeContext.Decode(value), remote: true));
        loop.Add("sync-branch", value => Select(p, ProbeContext.Decode(value), remote: false));
        foreach (var command in new[] { "sync-fetch", "sync-pull", "sync-push" }) loop.Add(command, () => Run(p, command[5..], wait: true));
        loop.Add("sync-start", name => Run(p, name, wait: false));
    }

    private static void ReportState(ProbeContext p)
    {
        var sync = p.Shell.Sync;
        Console.WriteLine($"TRAG_SYNC {ProbeContext.Flag(sync.IsSyncBusy)} {ProbeContext.Flag(p.Shell.Repository.IsWriting)} {ProbeContext.Flag(sync.FetchCommand.CanExecute(null))} {ProbeContext.Flag(sync.PullCommand.CanExecute(null))} {ProbeContext.Flag(sync.PushCommand.CanExecute(null))} {sync.Selection.Remote?.Name ?? "-"} {sync.Selection.SelectedBranch?.Branch.SourceRef ?? "-"}");
    }

    private static void Select(ProbeContext p, string value, bool remote) => Dispatcher.UIThread.Post(async () =>
    {
        p.Window.UpdateLayout();
        var selection = p.Shell.Sync.Selection;
        if (remote) p.Shell.Sync.ChooseRemoteCommand.Execute(selection.RemoteRows.Single(item => item.Name == value));
        else p.Shell.Sync.ChooseBranchCommand.Execute(selection.Branches.Single(item => item.Branch.SourceRef == value));
        await p.Shell.Sync.SyncCompletion;
        Console.WriteLine("TRAG_SYNC_SELECTION_OK");
    });

    private static void Run(ProbeContext p, string name, bool wait) => Dispatcher.UIThread.Post(async () =>
    {
        var button = p.Surface.Find<Button>(name switch { "fetch" => "FetchButton", "pull" => "PullButton", _ => "PushButton" });
        if (!await ProbeContext.PressAsync(button, wait ? null : () => Console.WriteLine("TRAG_SYNC_STARTED")))
        { Console.WriteLine("TRAG_SYNC_DISABLED"); return; }
        await p.Shell.Sync.SyncCompletion;
        Console.WriteLine(wait ? "TRAG_SYNC_RESULT " + p.Shell.StatusBar.SyncOperationText : "TRAG_SYNC_FINISHED");
    });
}
