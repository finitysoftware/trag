using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs.Settings;
using Trag.Domain.Accounts;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Dialogs.Settings;

// How the settings window lays out what it shows: the chosen section marked in the app's accent, in each provider's card
// an account's check at the left and its other actions at the right, with room between them and the account's lines and
// no more than the card's own padding below them, and at the end, as in a dialog, a line with the status and Done under it.
public sealed class SettingsLayoutTests
{
    private static FakeAccountService Service() => new()
    {
        Accounts =
        {
            new("one", AccountProvider.GitHub, "1", "alice", new("https://api.github.com/"), new("https://github.com/"), true, DateTimeOffset.UtcNow, null),
            new("two", AccountProvider.GitLab, "2", "bob", new("https://gitlab.com/api/v4/"), new("https://gitlab.com/"), true, DateTimeOffset.UtcNow, null)
        }
    };

    [AvaloniaFact]
    public async Task WhenASectionIsChosen_ShouldMarkItInTheAppsAccent()
    {
        var window = SettingsHarness.Window(Service()); window.Show(); await window.Initialization; AppHarness.Layout(window);

        var bar = window.Descendant<RadioButton>("IntegrationsSection")!.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_Bar");

        Assert.True(window.TryFindResource("AccentBrush", window.ActualThemeVariant, out var accent));
        Assert.Equal(((ISolidColorBrush)accent!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(bar.Background).Color);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenAccountsAreListed_ShouldPutTheCheckAtTheLeftAndTheOtherActionsAtTheRightUnderTheirLines()
    {
        var window = SettingsHarness.Window(Service()); window.Show(); await window.Initialization; AppHarness.Layout(window);

        foreach (var view in new Control[] { window.GetVisualDescendants().OfType<GitHubAccountCard>().Single(), window.GetVisualDescendants().OfType<GitLabAccountCard>().Single() })
        {
            var card = view.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("card"));
            Rect Placed(Control control) => new(control.TranslatePoint(default, card)!.Value, control.Bounds.Size);
            var identity = Placed(view.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text?.StartsWith("Identity checked", StringComparison.Ordinal) == true));
            var check = Placed(view.GetVisualDescendants().OfType<Button>().Single(button => button.Content is "Check connection"));
            var others = view.GetVisualDescendants().OfType<Button>().Where(button => button.Content is "Replace token" or "Remove").Select(Placed).ToArray();
            var actions = others.Append(check).ToArray();
            var edge = card.Padding.Right + card.BorderThickness.Right;

            Assert.True(check.Left <= card.Padding.Left + card.BorderThickness.Left + 0.5, $"{view.GetType().Name}: the check starts {check.Left} from the left");
            Assert.True(card.Bounds.Width - others.Max(action => action.Right) <= edge + 0.5, $"{view.GetType().Name}: the other actions end {card.Bounds.Width - others.Max(action => action.Right)} from the right");
            Assert.True(actions.Min(action => action.Top) - identity.Bottom >= 12, $"{view.GetType().Name}: the actions start {actions.Min(action => action.Top) - identity.Bottom} under the lines");
            Assert.True(card.Bounds.Height - actions.Max(action => action.Bottom) <= edge + 0.5, $"{view.GetType().Name}: the actions end {card.Bounds.Height - actions.Max(action => action.Bottom)} above the bottom");
        }
        window.Close();
    }

    // Wide enough for the status to take one line, lower than Done, so the two share a middle.
    [AvaloniaFact]
    public async Task WhenSettingsShows_ShouldEndWithALineAndTheStatusBesideDoneUnderIt()
    {
        var window = SettingsHarness.Window(Service()); window.Width = 1400; window.Show(); await window.Initialization; AppHarness.Layout(window);
        Rect Placed(Control control) => new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

        var (accounts, line, status, done) = (Placed(window.Descendant<ScrollViewer>("AccountsScroll")!), Placed(window.Descendant<Separator>("SettingsFooterLine")!),
            Placed(window.Descendant<TextBlock>("AccountStatus")!), Placed(window.GetVisualDescendants().OfType<Button>().Single(button => button.Content is "Done")));

        Assert.True(line.Top >= accounts.Bottom && line.Bottom <= Math.Min(status.Top, done.Top), $"the line is at {line.Top}, the accounts end at {accounts.Bottom}, the status starts at {status.Top}");
        Assert.True(status.Height < done.Height, $"the status is {status.Height} tall, Done {done.Height}");
        Assert.True(Math.Abs(status.Center.Y - done.Center.Y) <= 1, $"the status's middle is at {status.Center.Y}, Done's at {done.Center.Y}");
        Assert.True(status.Right <= done.Left, $"the status ends at {status.Right}, Done starts at {done.Left}");
        window.Close();
    }
}
