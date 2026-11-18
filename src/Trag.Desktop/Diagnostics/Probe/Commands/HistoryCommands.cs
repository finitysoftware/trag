using Avalonia.Controls;
using Avalonia.Threading;
using Trag.Domain.Presentation;

namespace Trag.Desktop.Diagnostics.Probe.Commands;

// History and search: reading more, the commit and file selection, a text check on the commit diff, and the search box. One registration per command; the search box has one
// method for its two variants.
internal static class HistoryCommands
{
    public static void Register(ProbeCommandLoop loop, ProbeContext p)
    {
        loop.Add("history-state", () => Dispatcher.UIThread.Post(p.ReportHistory));
        loop.Add("history", () => Dispatcher.UIThread.Post(async () => { p.Shell.Navigator.ShowHistoryCommand.Execute(null); await p.Shell.History.HistoryCompletion; p.ReportHistory(); }));
        loop.Add("history-more", () => Dispatcher.UIThread.Post(async () => { await p.Shell.History.LoadMoreHistoryCommand.ExecuteAsync(null); p.ReportHistory(); }));
        loop.Add("search", rest => Search(p, ProbeContext.Decode(rest), wait: true));
        loop.Add("search-start", rest => Search(p, ProbeContext.Decode(rest), wait: false));
        loop.Add("history-select", rest =>
        {
            var index = ProbeContext.Int(rest);
            Dispatcher.UIThread.Post(async () =>
            {
                p.Shell.History.SelectedCommit = p.Shell.History.Page.Commits[index]; await p.Shell.History.Details.Completion;
                Console.WriteLine($"TRAG_COMMIT {p.Shell.History.Details.Panel.Heading} {p.Shell.History.Details.Panel.Files.Count}");
            });
        });
        loop.Add("history-file", rest =>
        {
            var index = ProbeContext.Int(rest);
            Dispatcher.UIThread.Post(async () => { p.Shell.History.Details.Panel.SelectedFile = p.Shell.History.Details.Panel.Files[index]; await p.Shell.History.Details.Diff.Completion; Console.WriteLine("TRAG_COMMIT_DIFF " + p.Shell.History.Details.Diff.Document.Lines.Count); });
        });
        loop.Add("history-contains", text => Dispatcher.UIThread.Post(() =>
            Console.WriteLine("TRAG_HISTORY_CONTAINS " + ProbeContext.Flag(ProbeContext.Shows(p.Shell.History.Details.Diff.Document, text)))));
    }

    // The search box of the history view: "search" waits for the result, "search-start" only starts it.
    private static void Search(ProbeContext p, string query, bool wait) => Dispatcher.UIThread.Post(async () =>
    {
        if (p.Shell.Navigator.Section != WorkspaceSection.Search) p.Shell.Navigator.ShowSearchCommand.Execute(null);
        p.Window.UpdateLayout();
        ProbeSurface.Find<TextBox>(p.Surface.HistoryView, "HistorySearchEditor").Text = query;
        if (wait) { await p.Shell.History.HistoryCompletion; p.ReportHistory(); }
        else Console.WriteLine("TRAG_SEARCH_STARTED");
    });
}
