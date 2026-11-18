using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Trag.Desktop.UnitTests.Views;

// The small ✕ in a chip, which takes a co-author out of the message or leaves a file's history, is a square, as the ✕ of
// a dialog is.
public sealed class ChipCloseButtonTests
{
    [AvaloniaFact]
    public async Task WhenACoAuthorChipShows_ShouldTakeItOutWithASquareButton()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        window.Descendant<TextBox>("CommitMessageEditor")!.Text = "Fix the parser\n\nCo-authored-by: Ana <ana@example.invalid>"; AppHarness.Layout(window);

        AssertSquare(window.Descendant<Button>("RemoveCoAuthorButton")!);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheHistoryFollowsAFile_ShouldLeaveItWithASquareButton()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.History.ShowFileHistory("src/a.txt");
        await model.History.HistoryCompletion; AppHarness.Layout(window);

        AssertSquare(window.Descendant<Button>("ClearFileHistoryButton")!);
        window.Close();
    }

    private static void AssertSquare(Button button) =>
        Assert.True(button.Bounds.Width == button.Bounds.Height, $"{button.Name} is {button.Bounds.Width} wide and {button.Bounds.Height} tall");
}
