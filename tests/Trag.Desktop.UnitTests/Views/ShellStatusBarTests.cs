using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

// The window's bottom edge: the Cancel the shell offers, and the Details log of the last operation's Git output.
public sealed class ShellStatusBarTests
{
    // The shell's Cancel, as Esc, reaches the repository list's read and a clone, so the status bar offers it while either
    // runs, also over the repository list, where nothing else would say that Esc stops them.
    [AvaloniaFact]
    public async Task WhenACloneOrACatalogReadRuns_ShouldShowCancel()
    {
        var h = new AppHarness();
        var account = FakeAccountService.GitHub(user: "contoso");
        h.Catalog.Accounts.Add(account);
        h.Catalog.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, null)];
        h.Catalog.AccountsGate.IsClosed = true;
        await using var model = h.Shell();
        var window = h.Window(model);
        var cancel = window.Descendant<Button>("StatusCancelButton")!;
        Assert.False(cancel.IsEffectivelyVisible);

        model.Navigator.ShowRepositories();
        await h.Catalog.AccountsGate.Started;
        AppHarness.Layout(window);
        var whileReading = cancel.IsEffectivelyVisible;
        h.Catalog.AccountsGate.Open();
        await model.Catalog.CatalogCompletion;
        AppHarness.Layout(window);
        var afterReading = cancel.IsEffectivelyVisible;

        var group = Assert.Single(model.Catalog.AccountGroups);
        await group.LoadCommand.ExecuteAsync(null);
        model.Catalog.BeginCloneCommand.Execute(Assert.Single(group.Rows));
        h.Catalog.CloneGate.IsClosed = true;
        var cloning = model.Catalog.Clone.ConfirmCloneCommand.ExecuteAsync(null);
        await h.Catalog.CloneGate.Started;
        AppHarness.Layout(window);
        var whileCloning = cancel.IsEffectivelyVisible;
        h.Catalog.CloneGate.Open();
        await cloning;
        AppHarness.Layout(window);

        Assert.Equal((true, false, true, false), (whileReading, afterReading, whileCloning, cancel.IsEffectivelyVisible));
        window.Close();
    }

    // A refresh of the shown repository reads it, so the status bar offers Cancel until the read is done.
    [AvaloniaFact]
    public async Task WhenTheShownRepositoryIsRead_ShouldShowCancel()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        var cancel = window.Descendant<Button>("StatusCancelButton")!;
        h.Reader.Gate.IsClosed = true;
        var read = h.Reader.Gate.Arrival(h.Reader.Gate.Entries + 1);

        var refreshing = model.Session.RefreshAsync();
        await read;
        AppHarness.Layout(window);
        var whileReading = cancel.IsEffectivelyVisible;
        h.Reader.Gate.Open();
        await refreshing;
        AppHarness.Layout(window);

        Assert.Equal((true, false), (whileReading, cancel.IsEffectivelyVisible));
        window.Close();
    }

    // The log keeps its height and scrolls through output longer than it, and an error longer than the window is wide
    // wraps in its banner.
    [AvaloniaFact]
    public async Task WhenDetailsAreLongerThanTheLog_ShouldScroll()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.StatusBar.ErrorMessage = string.Concat(Enumerable.Repeat("Git kept the current branch: your local changes would be overwritten. ", 30));
        model.StatusBar.WriteDetails = string.Join("\n", Enumerable.Range(1, 60).Select(line => "error: line " + line + " of what Git printed"));
        model.IsSyncDetailsExpanded = true;
        AppHarness.Layout(window);

        var log = window.Descendant<ScrollViewer>("DetailsLog")!;
        Assert.True(log.IsEffectivelyVisible);
        Assert.InRange(log.Bounds.Height, 1, 140);
        Assert.True(log.Extent.Height > log.Viewport.Height, "The log does not scroll through the details.");
        log.Offset = new Vector(0, log.Extent.Height);
        AppHarness.Layout(window);
        Assert.True(log.Offset.Y > 0, "The log does not scroll through the details.");
        var banner = window.Descendant<Border>("ErrorBanner")!;
        Assert.True(((TextBlock)banner.Child!).TextLayout.TextLines.Count > 1, "The error runs off the window instead of wrapping.");
        window.Close();
    }
}
