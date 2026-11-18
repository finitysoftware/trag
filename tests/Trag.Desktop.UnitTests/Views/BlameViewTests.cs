using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Trag.Desktop.Controls;
using Trag.Desktop.Views.History;
using Trag.Domain.Blame;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.UnitTests.Views;

// The blame view over the shown section: the file's path and version with a way back, its lines with the commit named on
// the first line of each block and a stripe as strong as the commit is new, and the chosen block's commit with its two
// actions. Escape goes back too.
public sealed class BlameViewTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly BlameCommit First = new(Snapshots.Id('a'), "Ana", Now.AddDays(-30), "Add a", Snapshots.Path("a.txt"));
    private static readonly BlameCommit Second = new(Snapshots.Id('b'), "Ana", Now.AddDays(-2), "Change a", Snapshots.Path("a.txt"))
    {
        Previous = Snapshots.Id('a'), PreviousPath = Snapshots.Path("a.txt")
    };

    private static async Task<(ShellViewModel Model, Window Window)> BlamedAsync(AppHarness h)
    {
        h.History.Blames = (_, _) => new([new(1, "one", First), new(2, "two", First), new(3, "TWO", Second)]);
        var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Blame.OpenAsync("src/a.txt", null);
        AppHarness.Layout(window);
        return (model, window);
    }

    private static ListBoxItem[] Rows(Window window) => [.. window.Descendant<ListBox>("BlameLines")!.GetVisualDescendants().OfType<ListBoxItem>()];

    private static string[] Texts(Visual visual) => [.. visual.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible)
        .Select(text => text is CodeTextBlock code ? code.Code ?? "" : text.Text ?? "")];

    [AvaloniaFact]
    public async Task WhenAFileIsBlamed_ShouldCoverTheSectionWithItsLinesUntilTheWayBack()
    {
        var (model, window) = await BlamedAsync(new AppHarness());
        await using var _ = model;

        var view = window.Descendant<BlameView>("BlameOverlay")!;
        Assert.True(view.IsEffectivelyVisible);
        Assert.Contains("src/a.txt", Texts(view.Descendant<Border>("BlameHeader")!));
        Assert.Contains(BlameText.Version(null), Texts(view.Descendant<Border>("BlameHeader")!));
        var rows = Rows(window);
        Assert.Equal(3, rows.Length);
        Assert.Equal([model.Blame.Rows[0].Label, "1", "one"], Texts(rows[0]));
        Assert.Equal(["2", "two"], Texts(rows[1]));
        Assert.True(rows[0].Descendant<Border>("AgeStripe")!.Opacity < rows[2].Descendant<Border>("AgeStripe")!.Opacity);

        var back = window.Descendant<Button>("CloseBlameButton")!;
        Assert.Equal(("Back", "Back"), (ToolTip.GetTip(back), AutomationProperties.GetName(back)));
        Click(window, back);
        AppHarness.Layout(window);
        Assert.Equal((false, false), (model.Blame.IsOpen, view.IsEffectivelyVisible));
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenALineIsChosen_ShouldNameItsCommitWithItsTwoActions()
    {
        var (model, window) = await BlamedAsync(new AppHarness());
        await using var _ = model;
        var bar = window.Descendant<Border>("BlameCommitBar")!;
        Assert.False(bar.IsEffectivelyVisible);

        Click(window, Rows(window)[2]);
        AppHarness.Layout(window);

        Assert.Same(model.Blame.Rows[2], model.Blame.SelectedRow);
        Assert.True(bar.IsEffectivelyVisible);
        Assert.Contains(BlameText.Summary(Second), Texts(bar));
        var (show, before) = (bar.Descendant<Button>("ShowBlameCommitButton")!, bar.Descendant<Button>("BlameBeforeButton")!);
        Assert.Equal(("Show commit", "Blame before it"), (show.Content, before.Content));
        Assert.Equal((model.Blame.ShowCommitCommand, model.Blame.BlameBeforeCommand), (show.Command, before.Command));
        window.Close();
    }

    // Code is colored by the file's extension as the diff colors it, in the window's theme.
    [AvaloniaFact]
    public async Task WhenTheFileIsCode_ShouldColorItsSyntaxInTheThemesColors()
    {
        var h = new AppHarness();
        h.History.Blames = (_, _) => new([new(1, "using X; // note", First)]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Blame.OpenAsync("src/A.cs", null);
        AppHarness.Layout(window);
        var code = Rows(window)[0].GetVisualDescendants().OfType<CodeTextBlock>().Single();
        IBrush? Of(string text) => code.Inlines!.OfType<Run>().Single(run => run.Text == text).Foreground;

        Assert.Same(SyntaxColors.For(false)[(int)SyntaxKind.Keyword], Of("using"));
        Assert.Same(SyntaxColors.For(false)[(int)SyntaxKind.Comment], Of("// note"));
        Assert.False(code.Inlines!.OfType<Run>().Single(run => run.Text == " X; ").IsSet(TextElement.ForegroundProperty));
        window.RequestedThemeVariant = ThemeVariant.Dark;
        AppHarness.Layout(window);
        Assert.Same(SyntaxColors.For(true)[(int)SyntaxKind.Keyword], Of("using"));
        window.RequestedThemeVariant = ThemeVariant.Light; AppHarness.Layout(window);
        window.RequestedThemeVariant = Trag.Desktop.Themes.AppThemes.DarkEspresso; AppHarness.Layout(window);
        Assert.Same(SyntaxColors.For(true)[(int)SyntaxKind.Keyword], Of("using"));
        window.Close();
    }

    // The lines take the focus when the blame opens, so Escape reaches it without a click.
    [AvaloniaFact]
    public async Task WhenEscapeIsPressedRightAfterItOpens_ShouldGoBack()
    {
        var (model, window) = await BlamedAsync(new AppHarness());
        await using var _ = model;

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        AppHarness.Layout(window);

        Assert.False(model.Blame.IsOpen);
        window.Close();
    }

    // The header offers Git's two ways to look past a change: whitespace ignored, and moved lines followed.
    [AvaloniaFact]
    public async Task WhenAnOptionIsTickedInTheHeader_ShouldBlameTheFileAgainWithIt()
    {
        var h = new AppHarness();
        var (model, window) = await BlamedAsync(h);
        await using var _ = model;
        var (whitespace, moves) = (window.Descendant<CheckBox>("IgnoreWhitespaceBox")!, window.Descendant<CheckBox>("DetectMovesBox")!);

        Assert.Equal(("Ignore whitespace", "Detect moved lines"), ((string)whitespace.Content!, (string)moves.Content!));
        Click(window, whitespace);
        await model.Repository.Blame.Blame.Completion;
        Click(window, moves);
        await model.Repository.Blame.Blame.Completion;

        Assert.Equal(new BlameOptions(IgnoreWhitespace: true, DetectMoves: true), h.History.LastBlameOptions);
        window.Close();
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
}
