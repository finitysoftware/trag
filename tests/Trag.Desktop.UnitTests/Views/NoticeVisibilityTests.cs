using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.UnitTests.Views;

// The status bar's error, notice and repository warning are raised from every section: Save this version and Open from a
// commit's file in History, a refused close while History shows, a failed restore while the repository list is the first
// screen, an automatic fetch that stopped. Whatever section shows, the user has to be able to read them.
public sealed class NoticeVisibilityTests
{
    private const string Text = "Saved a.txt from 1234567 as C:\\Users\\me\\a.txt.";

    private static bool Shows(Window window, string text) =>
        window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == text && block.IsEffectivelyVisible);

    private static void Show(ShellViewModel model, string section)
    {
        if (section == "history") model.Navigator.ShowHistoryCommand.Execute(null);
        else if (section == "search") model.Navigator.ShowSearchCommand.Execute(null);
        else if (section == "repositories") model.Navigator.ShowRepositories();
        else model.Navigator.ShowFileStatusCommand.Execute(null);
    }

    // The file status is where the banners live today: the first row shows the check reads a banner that is there.
    [AvaloniaTheory]
    [InlineData("filestatus", "notice")]
    [InlineData("history", "notice")]
    [InlineData("search", "notice")]
    [InlineData("repositories", "notice")]
    [InlineData("history", "error")]
    [InlineData("repositories", "error")]
    [InlineData("history", "warning")]
    public async Task WhenAMessageIsRaisedWhileAnotherSectionShows_ShouldStillBeReadable(string section, string kind)
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        Show(model, section); AppHarness.Layout(window);

        if (kind == "notice") model.StatusBar.Notice = Text;
        else if (kind == "error") model.StatusBar.ErrorMessage = Text;
        else model.StatusBar.RepositoryWarning = Text;
        AppHarness.Layout(window);

        Assert.True(Shows(window, Text), "Nothing on screen reads: " + Text);
        window.Close();
    }

    // The last repository was moved or deleted: the start opens the repository list instead, and the reason it did so must
    // be on that screen.
    [AvaloniaFact]
    public async Task WhenTheLastRepositoryCannotBeOpenedAtTheStart_ShouldSayWhyOnTheRepositoryList()
    {
        var h = new AppHarness();
        h.Store.Path = "D:\\Projects\\moved";
        h.Reader.Failure = new RepositoryException(RepositoryError.PathUnavailable);
        await using var model = h.Shell();
        var window = h.Window(model);

        await model.InitializeAsync(); AppHarness.Layout(window);

        Assert.NotNull(model.CurrentPage);
        Assert.Equal(SessionText.Error(RepositoryError.PathUnavailable), model.StatusBar.ErrorMessage);
        Assert.True(Shows(window, SessionText.Error(RepositoryError.PathUnavailable)), "The reason the repository did not open is not on screen.");
        window.Close();
    }
}
