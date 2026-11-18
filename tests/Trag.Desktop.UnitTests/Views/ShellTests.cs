using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Trag.Application.Diff;
using Trag.Desktop.Controls;
using Trag.Desktop.Controls.Diff;
using Trag.Desktop.Diagnostics.Preview;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Views;
using Trag.Desktop.Views.History;
using Trag.Desktop.Views.Merge;
using Trag.Desktop.Views.Sync;
using Trag.Domain.Accounts;
using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;
using Trag.Application.Status;

namespace Trag.Desktop.UnitTests.Views;

// The main window over a shell: what its lists, panes, banner and dialogs do with the state the shell hands them.
public sealed class ShellTests
{
    [AvaloniaFact]
    public async Task WhenTheMergeRefreshes_ShouldKeepTheSelectedConflictAndDraftUnlessANewMergeResetsTheForm()
    {
        var h = Merge();
        h.Reader.Snapshot = h.Reader.Snapshot with { Files = [Snapshots.Conflict("a"), Snapshots.Conflict("b")] };
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        window.UpdateLayout();
        var list = window.Descendant<ListBox>("ConflictList")!;
        var editor = window.Descendant<TextBox>("MergeMessageEditor")!;
        list.SelectedIndex = 1;
        editor.Text = "My review";
        await model.RefreshCommand.ExecuteAsync(null);
        window.UpdateLayout();
        Assert.Equal("My review", editor.Text);
        Assert.Equal("b", model.Resolution.SelectedConflict!.PathText);
        Assert.Same(model.Resolution.SelectedConflict, list.SelectedItem);

        model.Merge.RequestAbortMergeCommand.Execute(null);
        h.Reader.Snapshot = h.Reader.Snapshot with { Merge = h.Reader.Snapshot.Merge! with { IncomingHead = "another", Message = "New merge" } };
        await model.RefreshCommand.ExecuteAsync(null);
        window.UpdateLayout();
        Assert.Equal("New merge", editor.Text);
        Assert.False(model.Merge.Abort.IsOpen);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheStatusRefreshesWithSeveralFilesSelected_ShouldKeepThemSelectedForTheDiscardToCapture()
    {
        var h = Discard();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("b"), Snapshots.Untracked("c")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        window.UpdateLayout();
        var list = window.Descendant<ListBox>("UnstagedList")!;
        list.SelectedItems!.Add(list.Items[0]);
        list.SelectedItems.Add(list.Items[1]);
        h.Reader.Snapshot = h.Reader.Snapshot with { Files = [Snapshots.Modified("a"), Snapshots.Modified("b"), Snapshots.Untracked("c")] };
        await model.RefreshCommand.ExecuteAsync(null);
        window.UpdateLayout();
        Assert.Equal(2, list.SelectedItems.Count);
        Assert.Equal(["b", "c"], model.FileStatus.UnstagedSelection.Select(row => row.PathText).Order());
        model.Discard.DiscardSelectedCommand.Execute(null);
        Assert.Equal(["b", "c"], model.Discard.Confirmation.Subject!.Paths.Select(path => System.Text.Encoding.UTF8.GetString(path.Bytes)).Order());
        Assert.Contains("stays staged", model.Discard.Confirmation.Subject!.Description);
        Assert.Contains("1 untracked file", model.Discard.Confirmation.Subject!.Description);
        Assert.Empty(h.Writer.Requests);
        window.Close();
    }

    // A delta that replaces selected rows (their change is another now), inserts one ahead of the selection and removes a
    // selected one keeps every selected file that is still listed selected, the primary one too.
    [AvaloniaFact]
    public async Task WhenADeltaReplacesOrInsertsRows_ShouldKeepTheMultiSelection()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("b"), Snapshots.Modified("c"), Snapshots.Modified("d"), Snapshots.Modified("e")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);
        var list = window.Descendant<ListBox>("UnstagedList")!;
        list.SelectedItems!.Add(list.Items[0]);
        list.SelectedItems.Add(list.Items[1]);
        list.SelectedItems.Add(list.Items[2]);
        list.SelectedItems.Add(list.Items[3]);
        AppHarness.Layout(window);

        h.Reader.Snapshot = h.Reader.Snapshot with { Files = [Snapshots.Modified("a"), new FileStatus(Snapshots.Path("b"), '.', 'D'), new FileStatus(Snapshots.Path("d"), '.', 'D'), Snapshots.Modified("e")] };
        await model.RefreshCommand.ExecuteAsync(null);
        AppHarness.Layout(window);

        Assert.Equal(["b", "d", "e"], model.FileStatus.UnstagedSelection.Select(row => row.PathText).Order());
        Assert.Equal(["b", "d", "e"], list.SelectedItems.Cast<StatusRow>().Select(row => row.PathText).Order());
        Assert.Equal("b", model.FileStatus.SelectedStatus?.PathText);
        window.Close();
    }

    // A staged hunk leaves the file changed on both sides: its row stays selected where it was, and the list does not move
    // to it, scrolled away as it is. A file selected after that is scrolled to again.
    [AvaloniaFact]
    public async Task WhenAHunkIsStaged_ShouldKeepTheRowSelectedAndTheScrollPlace()
    {
        var h = new AppHarness();
        var files = Enumerable.Range(0, 300).Select(i => Snapshots.Modified($"file-{i:D3}")).ToArray();
        h.Reader.Snapshot = Snapshots.Create(files: files);
        h.Diff.Diff = (_, _, _) => new([], [new(' ', " a", 1, 1), new('-', "-b", 2), new('+', "+B", null, 2), new(' ', " c", 3, 3)], DiffContentKind.Text, "");
        h.Writer.Handle = (_, _) =>
        {
            h.Reader.Snapshot = h.Reader.Snapshot with { Files = [.. files.Select(file => file.Path.Display == "file-150" ? Snapshots.PartiallyStaged("file-150") : file)] };
            return Task.FromResult(new GitWriteResult(GitWriteOutcome.Succeeded, "Done"));
        };
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);
        var list = window.Descendant<ListBox>("UnstagedList")!;
        list.SelectedIndex = 150;
        await model.WorkingDiff.Diff.Completion;
        AppHarness.Layout(window);
        list.ScrollIntoView(20);
        AppHarness.Layout(window);
        string? FirstVisible() => list.GetRealizedContainers().OfType<ListBoxItem>().OrderBy(item => item.Bounds.Top).Select(item => (item.DataContext as StatusRow)?.PathText).FirstOrDefault();
        var first = FirstVisible();
        var offset = list.Scroll!.Offset;

        await model.WorkingDiff.TakeHunkCommand.ExecuteAsync(new HunkChoice(model.WorkingDiff.Diff.Document.Lines, new DiffBlock(1, 2).Lines));
        await AppHarness.Until(() => model.FileStatus.StagedFiles.Count == 1, "the staged half");
        AppHarness.Layout(window);

        Assert.Equal(("file-150", "file-150"), (model.FileStatus.SelectedUnstaged?.PathText, (list.SelectedItem as StatusRow)?.PathText));
        Assert.Equal((first, offset), (FirstVisible(), list.Scroll!.Offset));
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[280];
        AppHarness.Layout(window);
        Assert.Contains("file-280", list.GetRealizedContainers().Select(item => (item.DataContext as StatusRow)?.PathText));
        window.Close();
    }

    // An update that keeps the selected row, or takes it away, leaves nothing to select again: the next file selected is
    // scrolled to.
    [AvaloniaFact]
    public async Task WhenAnUpdateKeepsOrRemovesTheSelectedRow_ShouldScrollToTheNextSelectedFile()
    {
        var h = new AppHarness();
        var files = Enumerable.Range(0, 300).Select(i => Snapshots.Modified($"file-{i:D3}")).ToList();
        h.Reader.Snapshot = Snapshots.Create(files: files);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);
        var list = window.Descendant<ListBox>("UnstagedList")!;
        bool Shown(string path) => list.GetRealizedContainers().Any(item => (item.DataContext as StatusRow)?.PathText == path);
        async Task UpdateAsync(IEnumerable<FileStatus> next)
        {
            h.Reader.Snapshot = h.Reader.Snapshot with { Files = [.. next] };
            await model.RefreshCommand.ExecuteAsync(null);
            AppHarness.Layout(window);
        }
        StatusRow Row(string path) => (StatusRow)model.FileStatus.UnstagedFiles[model.FileStatus.UnstagedFiles.IndexOf(new StatusRow(Snapshots.Modified(path), false))];
        model.FileStatus.SelectedUnstaged = Row("file-000");
        AppHarness.Layout(window);

        await UpdateAsync([Snapshots.Modified("a"), .. files]);
        model.FileStatus.SelectedUnstaged = Row("file-280");
        AppHarness.Layout(window);
        Assert.True(Shown("file-280"));

        await UpdateAsync(files.Where(file => file.Path.Display != "file-280"));
        model.FileStatus.SelectedUnstaged = Row("file-010");
        AppHarness.Layout(window);
        Assert.True(Shown("file-010"));
        window.Close();
    }

    // Selected files that an update replaces are selected again without moving the list to them, also when the selected
    // one is among them and the edits come in several batches. A file selected after that is scrolled to again.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenADeltaReplacesRowsOfAMultiSelection_ShouldKeepTheScrollPlace(bool inBatches)
    {
        var h = new AppHarness();
        var files = Enumerable.Range(0, 300).Select(i => Snapshots.Modified($"file-{i:D3}")).ToArray();
        h.Reader.Snapshot = Snapshots.Create(files: files);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);
        var list = window.Descendant<ListBox>("UnstagedList")!;
        list.SelectedIndex = 150;
        list.SelectedItems!.Add(list.Items[10]);
        list.SelectedItems.Add(list.Items[280]);
        AppHarness.Layout(window);
        list.ScrollIntoView(20);
        AppHarness.Layout(window);
        string? FirstVisible() => list.GetRealizedContainers().OfType<ListBoxItem>().OrderBy(item => item.Bounds.Top).Select(item => (item.DataContext as StatusRow)?.PathText).FirstOrDefault();
        var first = FirstVisible();
        var offset = list.Scroll!.Offset;

        bool Replaced(int i) => inBatches ? i >= 100 : i is 10 or 280;
        h.Reader.Snapshot = h.Reader.Snapshot with { Files = [.. files.Select((file, i) => Replaced(i) ? new FileStatus(file.Path, '.', 'D') : file)] };
        await model.RefreshCommand.ExecuteAsync(null);
        AppHarness.Layout(window);

        Assert.Equal("file-150", model.FileStatus.SelectedUnstaged?.PathText);
        Assert.Equal(["file-010", "file-150", "file-280"], list.SelectedItems.Cast<StatusRow>().Select(row => row.PathText).Order());
        Assert.Equal((first, offset), (FirstVisible(), list.Scroll!.Offset));
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[290];
        AppHarness.Layout(window);
        Assert.Contains("file-290", list.GetRealizedContainers().Select(item => (item.DataContext as StatusRow)?.PathText));
        window.Close();
    }

    // An update that keeps the selected rows, or takes one away, leaves nothing to select again: a file added to the
    // selection after it is scrolled to.
    [AvaloniaFact]
    public async Task WhenAnUpdateKeepsOrRemovesRowsOfAMultiSelection_ShouldScrollToTheNextFileAdded()
    {
        var h = new AppHarness();
        var files = Enumerable.Range(0, 300).Select(i => Snapshots.Modified($"file-{i:D3}")).ToList();
        h.Reader.Snapshot = Snapshots.Create(files: files);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);
        var list = window.Descendant<ListBox>("UnstagedList")!;
        bool Shown(string path) => list.GetRealizedContainers().Any(item => (item.DataContext as StatusRow)?.PathText == path);
        async Task UpdateAsync(IEnumerable<FileStatus> next)
        {
            h.Reader.Snapshot = h.Reader.Snapshot with { Files = [.. next] };
            await model.RefreshCommand.ExecuteAsync(null);
            AppHarness.Layout(window);
        }
        StatusRow Row(string path) => (StatusRow)model.FileStatus.UnstagedFiles[model.FileStatus.UnstagedFiles.IndexOf(new StatusRow(Snapshots.Modified(path), false))];
        model.FileStatus.SelectedUnstaged = Row("file-000");
        list.SelectedItems!.Add(Row("file-005"));
        AppHarness.Layout(window);

        await UpdateAsync([Snapshots.Modified("a"), .. files]);
        list.SelectedItems.Add(Row("file-280"));
        AppHarness.Layout(window);
        Assert.True(Shown("file-280"));

        await UpdateAsync([Snapshots.Modified("a"), .. files.Where(file => file.Path.Display != "file-005")]);
        list.SelectedItems.Add(Row("file-010"));
        AppHarness.Layout(window);
        Assert.True(Shown("file-010"));
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheMergeSectionShows_ShouldCreateTheViewOnDemandVirtualizeTheConflictsAndBindTheForm()
    {
        var h = Merge();
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model, Width = 880, Height = 580 }; window.Show();
        Assert.Empty(window.GetVisualDescendants().OfType<MergeView>());
        await model.Session.OpenRepositoryAsync("fixture"); window.UpdateLayout();
        model.StatusBar.SyncOperationText = "Pull left a merge in progress. Resolve and stage conflicts, then finish the merge; if no conflicts remain, review the hook or signing output and retry Finish merge.";
        model.StatusBar.SyncDetails = "Merge output"; model.Merge.Message = "Merge commit into trunk\n\n# Conflicts:\n# file-00000.txt"; window.UpdateLayout();
        var panel = Assert.Single(window.GetVisualDescendants().OfType<MergeView>());
        Assert.True(panel.Descendant<ListBox>("ConflictList")!.Bounds.Height >= 48,
            $"Conflict list: {panel.Descendant<ListBox>("ConflictList")!.Bounds}; panel: {panel.Bounds}; window: {window.Bounds}; message: {panel.Descendant<TextBox>("MergeMessageEditor")!.Bounds}; actions: {((Control)panel.Descendant<Button>("StageResolutionButton")!.Parent!).Bounds}; banner: {panel.Descendant<Control>("MergeBanner")!.Bounds}; footer: {((Control)panel.Descendant<TextBox>("MergeMessageEditor")!.Parent!).Bounds}");
        Assert.InRange(panel.Descendant<ListBox>("ConflictList")!.GetRealizedContainers().Count(), 1, 60);
        Assert.Same(model.Resolution.StageResolutionCommand, panel.Descendant<Button>("StageResolutionButton")!.Command);
        Assert.False(panel.Descendant<Button>("FinishMergeButton")!.Command!.CanExecute(null));
        panel.Descendant<TextBox>("MergeMessageEditor")!.Text = "Reviewed message"; Assert.Equal("Reviewed message", model.Merge.Message);
        panel.Descendant<Button>("RequestAbortMergeButton")!.Command!.Execute(null); window.UpdateLayout();
        Assert.True(model.Merge.Abort.IsOpen); Assert.Empty(h.Writer.Requests);
        Assert.True(panel.Descendant<Button>("ConfirmAbortMergeButton")!.IsEffectivelyVisible);
        panel.Descendant<Button>("KeepMergeButton")!.Command!.Execute(null); Assert.False(model.Merge.Abort.IsOpen); Assert.Empty(h.Writer.Requests);
        model.Navigator.ShowHistoryCommand.Execute(null); window.UpdateLayout(); Assert.Empty(window.GetVisualDescendants().OfType<MergeView>());
        model.Navigator.ShowMergeCommand.Execute(null); window.UpdateLayout(); Assert.Single(window.GetVisualDescendants().OfType<MergeView>());
        window.Close();
    }

    // Behind Details the last sync output takes the place of the write's once that is gone.
    [AvaloniaFact]
    public async Task WhenAWriteIsRejected_ShouldShowOnlyItsMessageInTheBannerAndTheGitOutputBehindDetails()
    {
        var h = Merge();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        model.StatusBar.SyncDetails = "Merge output";
        model.Navigator.ShowFileStatusCommand.Execute(null);
        model.StatusBar.ErrorMessage = "Git kept the current branch: your local changes would be overwritten."; model.StatusBar.WriteDetails = "error: Your local changes to the following files would be overwritten by checkout:\n\tREADME.md";
        window.UpdateLayout();
        var banner = window.Descendant<Border>("ErrorBanner")!; var toggle = window.Descendant<Avalonia.Controls.Primitives.ToggleButton>("DetailsToggle")!; var log = window.Descendant<ScrollViewer>("DetailsLog")!;
        Assert.True(banner.IsEffectivelyVisible); Assert.DoesNotContain(banner.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("error:", StringComparison.Ordinal) == true);
        Assert.True(toggle.IsEffectivelyVisible); Assert.False(log.IsEffectivelyVisible); Assert.Equal(model.StatusBar.WriteDetails, model.StatusBar.OperationDetails);
        toggle.IsChecked = true; window.UpdateLayout();
        Assert.True(log.IsEffectivelyVisible); Assert.Contains(log.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == model.StatusBar.WriteDetails);
        model.StatusBar.WriteDetails = ""; window.UpdateLayout();
        Assert.Equal("Merge output", model.StatusBar.OperationDetails); Assert.True(toggle.IsEffectivelyVisible);
        model.StatusBar.SyncDetails = ""; window.UpdateLayout();
        Assert.False(model.StatusBar.HasOperationDetails); Assert.False(model.IsSyncDetailsExpanded); Assert.False(toggle.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenAControlIsDisabled_ShouldUseTheLightAppBrushesInsteadOfFluentGrey()
    {
        var application = Avalonia.Application.Current!;
        foreach (var key in new[] { "TextControlBackgroundDisabled", "ComboBoxBackgroundDisabled", "TextControlForegroundDisabled", "ComboBoxForegroundDisabled", "CheckBoxForegroundUncheckedDisabled" })
        {
            Assert.True(application.TryGetResource(key, Avalonia.Styling.ThemeVariant.Light, out var light)); Assert.True(application.TryGetResource(key, Avalonia.Styling.ThemeVariant.Dark, out var dark));
            Assert.NotEqual(((Avalonia.Media.ISolidColorBrush)light!).Color, ((Avalonia.Media.ISolidColorBrush)dark!).Color);
        }
        application.TryGetResource("TextControlBackgroundDisabled", Avalonia.Styling.ThemeVariant.Light, out var background);
        Assert.Equal(Avalonia.Media.Color.Parse("#F4F2EF"), ((Avalonia.Media.ISolidColorBrush)background!).Color);
        await using var model = new AppHarness().Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); window.UpdateLayout();
        // Without a repository the commit area is not laid out yet; the composer still holds the editor.
        var editor = window.Workspace.Composer.CommitMessageEditor;
        Assert.True(editor.IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenAPaneDividerShows_ShouldKeepAGrabbableTrackAroundAThinLine()
    {
        var h = Sync();
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show();
        await model.Session.OpenRepositoryAsync("fixture"); window.UpdateLayout();
        var divider = window.Descendant<PaneSplitter>("StatusFilesDivider")!;
        Assert.Equal(RatioGrid.SplitterThickness, divider.Bounds.Width); Assert.Equal(2, PaneSplitter.LineThickness);
        Assert.True(divider.Bounds.Height > 100); Assert.NotNull(divider.Background);
        // Hovering leaves the divider colour alone; the accent is reserved for keyboard focus.
        var middle = divider.TranslatePoint(new Point(divider.Bounds.Width / 2, divider.Bounds.Height / 2), window)!.Value;
        window.MouseMove(middle); window.UpdateLayout();
        Assert.Contains(":pointerover", divider.Classes);
        Assert.True(Avalonia.Application.Current!.TryGetResource("DividerBrush", window.ActualThemeVariant, out var dividerBrush));
        Assert.Equal(((Avalonia.Media.ISolidColorBrush)dividerBrush!).Color, ((Avalonia.Media.ISolidColorBrush)divider.Background!).Color);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Avalonia.Controls.ToolTip>(), tip => tip.Transitions is { Count: > 0 });
        window.Close();
    }

    [AvaloniaFact]
    public void WhenTheBusyBarRuns_ShouldSweepInOneSecondWithTheFluentIndicatorParts()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), new BusyBar().Cycle);
        Assert.True(Avalonia.Application.Current!.TryGetResource(typeof(BusyBar), null, out var resource));
        var theme = Assert.IsType<Avalonia.Styling.ControlTheme>(resource);
        var animations = theme.Children.OfType<Avalonia.Styling.Style>().SelectMany(style => style.Animations).OfType<Avalonia.Animation.Animation>().ToArray();
        Assert.Equal(2, animations.Length);
        Assert.All(animations, animation => Assert.Equal(TimeSpan.FromSeconds(1), animation.Duration));
        var bar = new BusyBar { Width = 200, Height = 2 };
        var window = new Window { Content = bar }; window.Show(); window.UpdateLayout();
        Assert.False(bar.IsVisible);
        bar.IsActive = true; window.UpdateLayout();
        Assert.True(bar.IsVisible);
        var borders = bar.GetVisualDescendants().OfType<Border>().ToArray();
        Assert.Contains(borders, border => border.Name == "IndeterminateProgressBarIndicator");
        Assert.Contains(borders, border => border.Name == "IndeterminateProgressBarIndicator2");
        Assert.Equal(80, bar.TemplateSettings.ContainerWidth); // 40 % of the 200 px track, as in the Fluent bar
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheToolbarShows_ShouldDrawTilesLargerThanTheirGlyphsCloseTogetherInTheAgreedOrder()
    {
        await using var model = new AppHarness().Shell();
        model.Repository.Apply(Snapshots.Create());
        var window = new MainWindow { DataContext = model }; window.Show(); window.UpdateLayout();
        var open = window.Descendant<Button>("OpenButton")!; var commit = window.Descendant<Button>("PullButton")!;
        var tile = open.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("iconTile"));
        var glyph = tile.GetVisualDescendants().OfType<LineIcon>().Single();
        Assert.Equal(40, tile.Bounds.Width); Assert.Equal(40, tile.Bounds.Height); Assert.Equal(19, glyph.Bounds.Width);
        // Neighbouring tiles sit about 16 px apart in the app (MinWidth 54 minus the 40 px tile, plus 2 px spacing); before it was 33 px.
        Assert.Equal(54, open.MinWidth); Assert.Equal(new Thickness(5, 5), open.Padding); Assert.Equal(54, commit.MinWidth);
        Assert.Equal(2, ((StackPanel)open.GetVisualParent()!).Spacing);
        // The Commit button is gone because it never committed (Ctrl+K reaches the message, see ShortcutTests),
        // and Merge sits right after Fetch.
        Assert.Equal(["OpenButton", "PullButton", "PushButton", "PublishButton", "FetchButton", "MergeButton", "RefreshButton", "StashButton", "CloseButton"],
            ((StackPanel)open.GetVisualParent()!).Children.OfType<Button>().Select(button => button.Name ?? "").ToArray());
        window.Close();
    }

    // The navigation buttons carry the same chrome as each other and a different one from the toolbar.
    [AvaloniaFact]
    public async Task WhenTheSidebarShows_ShouldDrawEachNavigationButtonAsOneRowOfAnAccentGlyphAndALabel()
    {
        await using var model = new AppHarness().Shell();
        model.Repository.Apply(Snapshots.Create());
        var window = new MainWindow { DataContext = model }; window.Show(); window.UpdateLayout();
        var history = window.Descendant<Button>("HistoryButton")!;
        var row = (StackPanel)history.GetVisualDescendants().OfType<StackPanel>().First(panel => panel.Orientation == Avalonia.Layout.Orientation.Horizontal);
        var glyph = row.GetVisualDescendants().OfType<LineIcon>().Single();
        Assert.Equal(12, row.Spacing);
        Assert.Equal(18, glyph.Bounds.Width); Assert.Equal(18, glyph.Bounds.Height);
        Assert.True(Avalonia.Application.Current!.TryGetResource("AccentBrush", window.ActualThemeVariant, out var accent));
        Assert.Equal(((Avalonia.Media.ISolidColorBrush)accent!).Color, ((Avalonia.Media.ISolidColorBrush)glyph.Foreground!).Color);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, row.GetVisualDescendants().OfType<TextBlock>().Single().VerticalAlignment);
        // The style stretches the button and its content; only the content alignment is overridden, to the left.
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, history.HorizontalAlignment);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, history.HorizontalContentAlignment);
        Assert.Equal(12d, history.FontSize); Assert.Equal(new Thickness(10, 9), history.Padding);
        var nav = window.Descendant<Border>("Sidebar")!.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("nav")).ToArray();
        Assert.Equal(7, nav.Length);
        Assert.Equal(["", "HistoryButton", "SearchButton", "MergeButton", "RebaseButton", "PickButton", "ConflictsButton"], nav.Select(button => button.Name ?? "").ToArray());
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheCommitAreaShows_ShouldKeepEqualSpaceAboveAndBelowTheCommitButtonWithoutHintLines()
    {
        await using var model = new AppHarness().Shell();
        model.Repository.Apply(Snapshots.Create());
        var window = new MainWindow { DataContext = model }; window.Show(); window.UpdateLayout();
        var area = window.Descendant<Border>("CommitArea")!; var editor = window.Descendant<TextBox>("CommitMessageEditor")!; var button = window.Descendant<Button>("CommitButton")!;
        Assert.True(area.IsEffectivelyVisible);
        var above = button.TranslatePoint(default, area)!.Value.Y - editor.TranslatePoint(new Point(0, editor.Bounds.Height), area)!.Value.Y;
        var below = area.Bounds.Height - button.TranslatePoint(new Point(0, button.Bounds.Height), area)!.Value.Y;
        Assert.InRange(above, 7.5, 8.5); Assert.InRange(below, 7.5, 8.5);
        Assert.DoesNotContain(area.GetVisualDescendants().OfType<TextBlock>(), block => block.Text?.Contains("Stage adds every change") == true);
        Assert.DoesNotContain(area.GetVisualDescendants().OfType<TextBlock>(), block => block.Text?.Contains("Review the staged result") == true);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenUnstagedFilesAreSelected_ShouldEnableTheHeadersIconOnlyDiscardThatOpensTheRedConfirmation()
    {
        var h = Discard();
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show();
        await model.Session.OpenRepositoryAsync("fixture"); window.UpdateLayout();
        var button = window.Descendant<Button>("DiscardButton")!;
        Assert.IsType<LineIcon>(button.Content); Assert.Contains("headerdiscard", button.Classes); Assert.Equal("Discard the working tree changes in the selected files", ToolTip.GetTip(button));
        Assert.False(button.IsEffectivelyEnabled);
        var list = window.Descendant<ListBox>("UnstagedList")!;
        list.SelectedItems!.Add(model.FileStatus.UnstagedFiles[0]); window.UpdateLayout();
        Assert.True(button.IsEffectivelyEnabled);
        // The header keeps its order: title, discard, count, stage.
        var header = (Grid)button.GetVisualParent()!;
        Assert.Equal(1, Grid.GetColumn(button)); Assert.Equal(2, Grid.GetColumn(window.Descendant<Button>("StageButton")!));
        Assert.Equal(["Unstaged files", "·", "2"], window.Descendant<StackPanel>("UnstagedHeading")!.Children.OfType<TextBlock>().Select(block => block.Text));
        Assert.Equal(["Staged files", "·", "0"], window.Descendant<StackPanel>("StagedHeading")!.Children.OfType<TextBlock>().Select(block => block.Text));
        button.Command!.Execute(null); AppHarness.Layout(window);
        Assert.True(window.Descendant<DialogHost>("DiscardOverlay")!.IsEffectivelyVisible);
        Assert.Contains("danger", window.Descendant<Button>("ConfirmDiscardButton")!.Classes); Assert.True(window.Descendant<Button>("CancelDiscardButton")!.IsFocused);
        Assert.Contains("a.txt", window.Descendant<DialogHost>("DiscardOverlay")!.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text ?? ""));
        model.Discard.Confirmation.CancelCommand.Execute(null); window.UpdateLayout();
        Assert.False(window.Descendant<DialogHost>("DiscardOverlay")!.IsEffectivelyVisible); Assert.Empty(h.Writer.Requests);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenAnotherRemoteAndBranchAreChosen_ShouldBindTheSyncSelectorsToTheReviewedTargetAndToolbarPush()
    {
        var h = Sync();
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show();
        Assert.False(window.GetVisualDescendants().OfType<SyncPanel>().Single().IsEffectivelyVisible);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; window.UpdateLayout();
        var panel = Assert.Single(window.GetVisualDescendants().OfType<SyncPanel>());
        Assert.True(model.Publish.Context.HasMultipleRemotes); Assert.True(panel.Descendant<Button>("SyncRemoteSelector")!.IsEffectivelyVisible); Assert.True(panel.Descendant<TextBlock>("RemoteHeader")!.IsEffectivelyVisible);
        // The caption is reached through its own button rather than by a global name, because ToolButton builds it.
        var pushLabel = window.Descendant<Button>("PushButton")!.GetVisualDescendants().OfType<TextBlock>().Single();
        Assert.Equal("Push", pushLabel.Text); Assert.DoesNotContain("accent", pushLabel.Classes);
        Assert.Same(model.Sync.PushCommand, window.Descendant<Button>("PushButton")!.Command);
        Assert.True(model.Sync.PushCommand.CanExecute(null));
        model.Sync.ChooseRemoteCommand.Execute(model.Sync.Selection.RemoteRows[1]); await model.Sync.SyncCompletion;
        Assert.Equal("backup", model.Sync.Selection.Remote!.Name); Assert.Null(model.Sync.Selection.SelectedBranch); Assert.False(model.Sync.PushCommand.CanExecute(null));
        model.Sync.ChooseBranchCommand.Execute(model.Sync.Selection.Branches[0]); await model.Sync.SyncCompletion;
        Assert.True(model.Sync.PushCommand.CanExecute(null)); Assert.Contains("backup-push", model.Sync.Panel.Target.PushDestination);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenASyncReadRuns_ShouldShowTheBusyBarOnTheSidebarsBottomEdgeWithoutMovingTheContentAboveIt()
    {
        var h = Sync();
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show();
        window.Descendant<BusyBar>("SyncBusyBar")!.Cycle = TimeSpan.FromMilliseconds(80);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; window.UpdateLayout();
        var panel = Assert.Single(window.GetVisualDescendants().OfType<SyncPanel>());
        await ChooseTheBackupTarget(model, panel);
        var sidebar = window.Descendant<Border>("Sidebar")!; var slot = window.Descendant<Border>("SyncBusySlot")!; var bar = window.Descendant<BusyBar>("SyncBusyBar")!;
        Assert.Null(window.Descendant<TextBlock>("SidebarLogo"));
        Assert.Empty(panel.GetVisualDescendants().OfType<ProgressBar>());
        await AppHarness.Until(() => !bar.IsEffectivelyVisible, "the sync busy bar to finish its cycle"); window.UpdateLayout();
        Assert.False(model.Sync.IsSyncBusy); Assert.False(bar.IsEffectivelyVisible);
        Assert.Equal(2, slot.Bounds.Height); Assert.Equal(sidebar.Bounds.Height, slot.Bounds.Bottom);
        var panelBounds = panel.Bounds;
        h.Sync.ReadGate.IsClosed = true; model.Repository.Remotes.Reread(); window.UpdateLayout();
        Assert.True(model.Sync.IsSyncBusy); Assert.True(bar.IsEffectivelyVisible); Assert.Equal(2, bar.Bounds.Height); Assert.Equal(sidebar.Bounds.Height, slot.Bounds.Bottom);
        Assert.Equal(panelBounds, panel.Bounds);
        h.Sync.ReadGate.Open(); await model.Sync.SyncCompletion; window.UpdateLayout();
        // A quick operation still shows one full animation cycle before the bar goes away.
        Assert.True(bar.IsEffectivelyVisible); Assert.Equal(panelBounds, panel.Bounds);
        await AppHarness.Until(() => !bar.IsEffectivelyVisible, "the sync busy bar to hide after one cycle"); window.UpdateLayout();
        Assert.False(bar.IsEffectivelyVisible); Assert.Equal(panelBounds, panel.Bounds);
        window.Close();
    }

    // The first read of a target says nothing while it runs: the busy bar shows it, and the panel grows no line that
    // goes again when the read ends.
    [AvaloniaFact]
    public async Task WhenTheFirstReadOfATargetRuns_ShouldNotSayItIsReading()
    {
        var h = Sync();
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; window.UpdateLayout();
        var panel = Assert.Single(window.GetVisualDescendants().OfType<SyncPanel>());
        h.Sync.ReadGate.IsClosed = true;

        model.Sync.ChooseRemoteCommand.Execute(model.Sync.Selection.RemoteRows[1]); window.UpdateLayout();

        Assert.True(model.Sync.IsSyncBusy); Assert.Equal((null, SyncText.Reading), (model.Sync.Panel.State, model.Sync.Panel.Message));
        Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible && block.Text == SyncText.Reading);
        h.Sync.ReadGate.Open(); await model.Sync.SyncCompletion;
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenSeveralTragAccountsMatchTheRemote_ShouldOfferTheAccountChoiceInTheSidebarAndOnlyThen()
    {
        var external = new AccountChoice(AccountChoice.External, "Existing Git credentials / SSH agent");
        var alice = new AccountChoice("alice", "alice · https://gitlab.example");
        var bob = new AccountChoice("bob", "bob · https://gitlab.example");

        // The only matching account is already chosen, so there is nothing to decide in the sidebar.
        var single = Sync(new([external, alice], "alice", "Using alice, the only account matching this remote."));
        await using var one = single.Shell();
        var first = new MainWindow { DataContext = one }; first.Show();
        await one.Session.OpenRepositoryAsync("fixture"); await one.Sync.SyncCompletion; first.UpdateLayout();
        Assert.False(first.GetVisualDescendants().OfType<SyncPanel>().Single().Descendant<StackPanel>("SyncAccountSection")!.IsEffectivelyVisible);
        Assert.True(one.Sync.FetchCommand.CanExecute(null));
        first.Close();

        var h = Sync(new([external, alice, bob], "alice", "Using the saved account for this repository."));
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; window.UpdateLayout();
        var panel = window.GetVisualDescendants().OfType<SyncPanel>().Single();
        var selector = panel.Descendant<ComboBox>("SyncAccountSelector")!;
        Assert.True(panel.Descendant<StackPanel>("SyncAccountSection")!.IsEffectivelyVisible);
        Assert.Equal("alice", ((AccountChoice)selector.SelectedItem!).Id); Assert.False(panel.Descendant<Button>("SaveSyncAccountButton")!.IsEffectivelyVisible);
        Assert.True(model.Sync.FetchCommand.CanExecute(null));
        selector.SelectedItem = external; window.UpdateLayout();
        Assert.True(model.Sync.Panel.Accounts.IsUnsaved); Assert.True(panel.Descendant<Button>("SaveSyncAccountButton")!.IsEffectivelyVisible); Assert.False(model.Sync.FetchCommand.CanExecute(null));
        window.Close();

        // Only Git's own credentials available: nothing to choose, so the section stays out of the way.
        var plain = Sync(new([external], AccountChoice.External, "Using your existing Git credentials."));
        await using var other = plain.Shell();
        var second = new MainWindow { DataContext = other }; second.Show();
        await other.Session.OpenRepositoryAsync("fixture"); await other.Sync.SyncCompletion; second.UpdateLayout();
        Assert.False(second.GetVisualDescendants().OfType<SyncPanel>().Single().Descendant<StackPanel>("SyncAccountSection")!.IsEffectivelyVisible);
        second.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheSearchShows_ShouldCreateTheHistoryViewOnDemandWithAVirtualizedListAndABoundQuery()
    {
        var h = new AppHarness();
        var commits = Enumerable.Range(0, 100).Select(i => new HistoryCommit(new string('a', 40), "Author", "email", "2026-09-14", "Commit " + i)).ToArray();
        h.History.Pages = (_, page) => new(commits, false, page);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Empty(window.GetVisualDescendants().OfType<HistoryView>());
        model.Navigator.ShowSearchCommand.Execute(null); window.UpdateLayout();
        var view = Assert.Single(window.GetVisualDescendants().OfType<HistoryView>());
        view.Descendant<TextBox>("HistorySearchEditor")!.Text = "author query";
        Assert.Equal("author query", model.History.SearchQuery);
        // The search reads a page of a hundred commits; the list realizes only what it shows.
        await model.History.HistoryCompletion;
        window.UpdateLayout();
        Assert.InRange(view.Descendant<ListBox>("HistoryList")!.GetRealizedContainers().Count(), 1, 50);
        model.Navigator.ShowFileStatusCommand.Execute(null); window.UpdateLayout();
        Assert.Empty(window.GetVisualDescendants().OfType<HistoryView>());
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheSyntheticPreviewLoads_ShouldBindItsPanelsAndVirtualizeThem()
    {
        await using var model = new AppHarness().Shell();
        var window = new MainWindow { DataContext = model };
        new SyntheticPreview(model).Toggle();
        window.Show();
        Assert.Equal(10_000, window.Descendant<ListBox>("FilesList")!.ItemCount);
        Assert.Equal(50_000, window.Descendant<ListBox>("DiffList")!.ItemCount);
        Assert.True(window.Descendant<ListBox>("FilesList")!.GetRealizedContainers().Count() < 100);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheFileListsHoldTenThousandFiles_ShouldRealizeOnlyTheRowsTheyShow()
    {
        await using var model = new AppHarness().Shell();
        var window = TenThousandFilesShown(model);
        var staged = window.Descendant<ListBox>("StagedList")!;
        var unstaged = window.Descendant<ListBox>("UnstagedList")!;
        Assert.Equal(10_000, staged.ItemCount);
        Assert.Equal(10_000, unstaged.ItemCount);
        Assert.InRange(staged.GetRealizedContainers().Count(), 1, 60);
        Assert.InRange(unstaged.GetRealizedContainers().Count(), 1, 60);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenAFileIsSelectedInOneList_ShouldShowItsSourceAndClearTheOtherList()
    {
        await using var model = new AppHarness().Shell();
        var window = TenThousandFilesShown(model);
        var staged = window.Descendant<ListBox>("StagedList")!;
        var unstaged = window.Descendant<ListBox>("UnstagedList")!;
        staged.SelectedIndex = 0;
        Assert.Equal("Staged · index", model.FileStatus.SelectedStatus!.Source);
        unstaged.SelectedIndex = 0;
        Assert.Equal(-1, staged.SelectedIndex);
        Assert.Equal("Unstaged · working tree", model.FileStatus.SelectedStatus!.Source);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenSeveralFilesAreSelectedAndAMessageTyped_ShouldReachTheModelAndTheStageAndCommitButtons()
    {
        await using var model = new AppHarness().Shell();
        model.Repository.Apply(Snapshots.Create());
        model.FileStatus.UnstagedFiles = new(new StatusFiles(Enumerable.Range(0, 3).Select(i => new FileStatus(new(System.Text.Encoding.UTF8.GetBytes($"file-{i}.txt")), '?', '?')).ToArray(), false));
        var window = new MainWindow { DataContext = model };
        window.Show();
        var list = window.Descendant<ListBox>("UnstagedList")!;
        list.SelectedItems!.Add(model.FileStatus.UnstagedFiles[0]);
        list.SelectedItems.Add(model.FileStatus.UnstagedFiles[2]);
        Assert.Equal(2, model.FileStatus.UnstagedSelection.Count);
        Assert.Equal("Stage 2 files", window.Descendant<Button>("StageButton")!.Content);
        var editor = window.Descendant<TextBox>("CommitMessageEditor")!;
        editor.Text = "Message from the editor";
        Assert.Equal(editor.Text, model.Commit.CommitMessage);
        Assert.Same(model.Commit.CommitCommand, window.Descendant<Button>("CommitButton")!.Command);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheWindowClosesDuringAWrite_ShouldStayOpenUntilTheWriteIsVerified()
    {
        await using var model = new AppHarness().Shell();
        var window = TwoFilesSelectedAndAMessageTyped(model);
        model.Repository.IsWriting = true;
        window.Close();
        Assert.True(window.IsVisible);
        Assert.Contains("wait", model.StatusBar.Notice, StringComparison.OrdinalIgnoreCase);
        model.Repository.IsWriting = false;
        window.Close();
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task WhenADiffHasFiftyThousandRows_ShouldRealizeOnlyTheRowsItShowsEvenAtTheEnd()
    {
        await using var model = new AppHarness().Shell();
        model.Repository.Apply(Snapshots.Create());
        model.FileStatus.SelectedStatus = new(new(new("sample.cs"u8.ToArray()), '.', 'M'), false);
        var lines = Enumerable.Range(1, 50_000).Select(i => new PatchLine('+', "+sample", null, i)).ToArray();
        var fileDiff = new FileDiff([], lines, DiffContentKind.Text, "");
        model.WorkingDiff.Diff.Document = DiffDocument.From(fileDiff, await new BackgroundDiffBuilder().BuildAsync(fileDiff, "sample.cs", default));
        var window = new MainWindow { DataContext = model };
        window.Show();
        var diff = window.Descendant<DiffView>("FileDiffView")!.Descendant<ListBox>("Rows")!;
        Assert.Equal(50_000, diff.ItemCount);
        Assert.InRange(diff.GetRealizedContainers().Count(), 1, 100);
        diff.ScrollIntoView(49_999);
        window.UpdateLayout();
        Assert.InRange(diff.GetRealizedContainers().Count(), 1, 100);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenARefreshAppliesADelta_ShouldKeepTheSelectionAndTheScrolledViewport()
    {
        var changed = false;
        var h = new AppHarness();
        h.Reader.Read = (path, _) => Task.FromResult(StatusSnapshot(path, changed));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var window = new MainWindow { DataContext = model };
        window.Show();
        var list = window.Descendant<ListBox>("UnstagedList")!;
        var items = model.FileStatus.UnstagedFiles;
        list.SelectedIndex = 500;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
        list.ScrollIntoView(800);
        window.UpdateLayout();
        string? FirstVisible() => list.GetRealizedContainers().OfType<ListBoxItem>().Select(item => (item.DataContext as StatusRow)?.PathText).FirstOrDefault();
        var first = FirstVisible();
        changed = true;
        await model.RefreshCommand.ExecuteAsync(null);
        window.UpdateLayout();
        Assert.Same(items, model.FileStatus.UnstagedFiles);
        Assert.Equal("file-00500", model.FileStatus.SelectedStatus!.PathText);
        Assert.Equal(first, FirstVisible());
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheRepositoryClosesOrAnotherOneOpens_ShouldCloseTheOpenDialogs()
    {
        var h = new AppHarness(); h.Reader.Snapshot = h.Synced(Snapshots.Modified("a.txt"));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        model.Stash.OpenStashCommand.Execute(null); AppHarness.Layout(window);
        var stash = window.Descendant<DialogHost>("StashOverlay")!;
        Assert.True(stash.IsEffectivelyVisible); Assert.Equal("StashMessageEditor", (window.FocusManager!.GetFocusedElement() as Control)?.Name);
        await model.CloseRepositoryCommand.ExecuteAsync(null); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        Assert.False(model.Stash.Draft.IsOpen); Assert.False(stash.IsEffectivelyVisible); Assert.Equal(WorkspaceSection.Repositories, model.Navigator.Section);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        Assert.False(stash.IsEffectivelyVisible); Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section);
        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null); AppHarness.Layout(window);
        var create = window.Descendant<DialogHost>("CreateBranchOverlay")!;
        Assert.True(create.IsEffectivelyVisible);
        await model.Session.OpenRepositoryAsync("other"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        Assert.True(model.Repository.HasRepository); Assert.False(model.Branches.Draft.IsOpen); Assert.False(create.IsEffectivelyVisible);
        window.Close();
    }

    // One modified file and one untracked file, and a writer that always succeeds.
    private static AppHarness Discard()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt"), Snapshots.Untracked("b.txt")]);
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Done.");
        return h;
    }

    // A merge with ten thousand conflicts, so the list has to virtualize; every write is refused.
    private static AppHarness Merge()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(branch: "trunk", head: "head", operation: RepositoryOperation.Merge, identity: false,
            files: [.. Enumerable.Range(0, 10_000).Select(i => Snapshots.Conflict($"file-{i:D5}.txt"))],
            merge: new("incoming", "head", "Merge message", "merge", ""));
        h.Writer.Result = new(GitWriteOutcome.Rejected, "Fixture");
        return h;
    }

    // Two remotes with trunk tracking the first one; the sync read answers for whichever remote is asked about, and
    // no write may run.
    private static AppHarness Sync(RemoteAccountState? accounts = null)
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(branch: "trunk", trackingRemote: "origin", upstream: "origin/trunk", upstreamBranchRef: "refs/heads/trunk", identity: false,
            indexFingerprint: "", remotes: [new("origin", "origin-fetch", "origin-push"), new("backup", "backup-fetch", "backup-push")]);
        h.Sync.State = (snapshot, remote, branch) =>
        {
            var item = new SyncBranch("refs/heads/trunk", "refs/remotes/" + remote + "/trunk");
            return new(new(remote, remote + "-fetch", remote + "-push", "config"), [item],
                branch is not null || remote == snapshot.TrackingRemote ? item : null, 0, 0, "Local comparison") { Accounts = accounts };
        };
        h.Writer.Failure = new NotSupportedException();
        return h;
    }

    // The selector test's choices, the second remote and its first branch, so the busy bar runs over the target they
    // leave.
    private static async Task ChooseTheBackupTarget(ShellViewModel model, SyncPanel panel)
    {
        model.Sync.ChooseRemoteCommand.Execute(model.Sync.Selection.RemoteRows[1]); await model.Sync.SyncCompletion;
        model.Sync.ChooseBranchCommand.Execute(model.Sync.Selection.Branches[0]); await model.Sync.SyncCompletion;
    }

    // Ten thousand files, each both staged and unstaged, in a window over a repository that was never read.
    private static MainWindow TenThousandFilesShown(ShellViewModel model)
    {
        var files = Enumerable.Range(0, 10_000).Select(i => new FileStatus(new(System.Text.Encoding.UTF8.GetBytes($"file-{i}.txt")), 'M', 'M')).ToArray();
        model.Repository.Apply(Snapshots.Create());
        model.FileStatus.StagedFiles = new(new StatusFiles(files, true));
        model.FileStatus.UnstagedFiles = new(new StatusFiles(files, false));
        var window = new MainWindow { DataContext = model };
        window.Show();
        return window;
    }

    // The selection test's steps: two of three untracked files selected and a message typed, so the close meets the
    // form that test leaves.
    private static MainWindow TwoFilesSelectedAndAMessageTyped(ShellViewModel model)
    {
        model.Repository.Apply(Snapshots.Create());
        model.FileStatus.UnstagedFiles = new(new StatusFiles(Enumerable.Range(0, 3).Select(i => new FileStatus(new(System.Text.Encoding.UTF8.GetBytes($"file-{i}.txt")), '?', '?')).ToArray(), false));
        var window = new MainWindow { DataContext = model };
        window.Show();
        var list = window.Descendant<ListBox>("UnstagedList")!;
        list.SelectedItems!.Add(model.FileStatus.UnstagedFiles[0]);
        list.SelectedItems.Add(model.FileStatus.UnstagedFiles[2]);
        window.Descendant<TextBox>("CommitMessageEditor")!.Text = "Message from the editor";
        return window;
    }

    private static RepositorySnapshot StatusSnapshot(string path, bool changed)
    {
        var files = Enumerable.Range(0, 1000).Select(i => Snapshots.Modified($"file-{i:D5}")).ToList();
        if (changed)
        {
            files.Add(Snapshots.Untracked("a-new"));
            files[500] = files[500] with { Worktree = 'D' };
        }
        return Snapshots.Create(root: path, head: "abc", identity: false, indexFingerprint: "", files: files);
    }
}
