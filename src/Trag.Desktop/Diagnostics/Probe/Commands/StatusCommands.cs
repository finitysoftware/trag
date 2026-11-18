using Avalonia.Controls;
using Avalonia.Threading;
using Trag.ViewModels.Status;

namespace Trag.Desktop.Diagnostics.Probe.Commands;

// The file status section: the status line, selection by index, multi-selection through the list, the commit message,
// the write state, and stage/unstage/commit through the toolbar buttons.
internal static class StatusCommands
{
    public static void Register(ProbeCommandLoop loop, ProbeContext p)
    {
        loop.Add("status", () => Dispatcher.UIThread.Post(() => p.Report("STATUS")));
        loop.Add("select", rest => Select(p, rest));
        loop.Add("select-many", rest =>
        {
            var parts = rest.Split(' ');
            Dispatcher.UIThread.Post(() =>
            {
                var list = p.Surface.Find<ListBox>(parts[0] == "staged" ? "StagedList" : "UnstagedList");
                list.SelectedItems!.Clear();
                foreach (var index in parts.Skip(1).Select(int.Parse)) list.SelectedItems.Add(list.Items[index]);
                Console.WriteLine("TRAG_SELECTION_COUNT " + list.SelectedItems.Count);
            });
        });
        loop.Add("message", rest =>
        {
            var message = ProbeContext.Decode(rest);
            Dispatcher.UIThread.Post(() => { p.Surface.Find<TextBox>("CommitMessageEditor").Text = message; Console.WriteLine("TRAG_MESSAGE_OK"); });
        });
        loop.Add("write-state", () => Dispatcher.UIThread.Post(() =>
            Console.WriteLine($"TRAG_WRITE_STATE {ProbeContext.Flag(p.Shell.Commit.CommitMessage.Length != 0)} {ProbeContext.Flag(p.Shell.Repository.NeedsReview)} {ProbeContext.Flag(p.Shell.Repository.IsWriting)} {p.Shell.Repository.Writer.LastWrite?.Result?.CommitId}")));
        foreach (var command in new[] { "stage", "unstage", "commit" })
            loop.Add(command, () => RunWrite(p, command));
    }

    private static void Select(ProbeContext p, string rest)
    {
        var parts = rest.Split(' ');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var index)) return;
        var staged = parts[0] == "staged";
        Dispatcher.UIThread.Post(() =>
        {
            SelectRow(p, staged, index);
            // Programmatic selection needs the focus that a real click supplies.
            p.Window.Focus();
            Console.WriteLine("TRAG_SELECTION_" + (p.Shell.FileStatus.HasSelection ? "OK" : "FAILED"));
        });
    }

    private static void SelectRow(ProbeContext p, bool staged, int index)
    {
        var status = p.Shell.FileStatus;
        var list = staged ? status.StagedFiles : status.UnstagedFiles;
        if (index < 0 || index >= list.Count) return;
        if (staged) status.SelectedStaged = (StatusRow)list[index];
        else status.SelectedUnstaged = (StatusRow)list[index];
    }

    private static void RunWrite(ProbeContext p, string command) => Dispatcher.UIThread.Post(async () =>
    {
        var name = command switch { "stage" => "StageButton", "unstage" => "UnstageButton", _ => "CommitButton" };
        if (await ProbeContext.PressAsync(p.Surface.Find<Button>(name))) p.Report("WRITE");
        else Console.WriteLine("TRAG_WRITE_DISABLED");
    });
}
