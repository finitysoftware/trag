using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Trag.Desktop.Controls;
using Trag.Desktop.Controls.Diff;
using Trag.Desktop.Views;
using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

public sealed class HistoryModelsUiTests
{
    // Under the author the commit pane says who signed the commit and who co-authored it; a commit with neither shows
    // neither line.
    [AvaloniaFact]
    public async Task WhenTheChosenCommitIsSignedAndCoAuthored_ShouldSayBothUnderTheAuthor()
    {
        var h = new AppHarness();
        h.History.Details = id => id == Snapshots.HeadId
            ? new(Snapshots.Commit(id, "Fix"), "Fix\n\nCo-authored-by: Ana Anić <ana@example.invalid>\n", []) { Signature = new(SignatureCheck.Good, "ana@example.invalid") }
            : new(Snapshots.Commit(id, "Initial"), "Initial", []);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion; AppHarness.Layout(window);
        var signature = window.Descendant<TextBlock>("CommitSignatureText")!;
        var coAuthors = window.Descendant<TextBlock>("CommitCoAuthorText")!;

        Assert.Equal(("Signed by ana@example.invalid.", true), (signature.Text, signature.IsEffectivelyVisible));
        Assert.Equal(("with Ana Anić", true), (coAuthors.Text, coAuthors.IsEffectivelyVisible));
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion; AppHarness.Layout(window);
        Assert.False(signature.IsEffectivelyVisible);
        Assert.False(coAuthors.IsEffectivelyVisible);
        window.Close();
    }

    // The commit pane names the chosen commit by its full ID, which can be selected and copied like its message.
    [AvaloniaFact]
    public async Task WhenTheCommitPaneHeadingIsShown_ShouldLetTheIdBeSelectedAndCopied()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion; AppHarness.Layout(window);

        var heading = Assert.IsType<SelectableTextBlock>(window.Descendant<TextBlock>("CommitHeading"));
        heading.SelectAll();
        heading.Copy();

        Assert.Equal(Snapshots.HeadId, await window.Clipboard!.TryGetTextAsync());
        window.Close();
    }

    // The branch checked out stands out, a remote-tracking branch and a tag look quieter, and a commit without a name
    // shows only its message.
    [AvaloniaFact]
    public async Task WhenACommitIsTheTipOfBranchesOrTags_ShouldShowTheirBadgesBesideItsMessage()
    {
        var h = new AppHarness();
        h.History.Commits[0] = h.History.Commits[0] with { Refs = [new("main", CommitRefKind.CurrentBranch), new("origin/main", CommitRefKind.RemoteBranch)] };
        h.History.Commits[1] = h.History.Commits[1] with { Refs = [new("topic", CommitRefKind.Branch), new("v1.0", CommitRefKind.Tag)] };
        h.History.Commits.Add(Snapshots.Commit(Snapshots.Id('c'), "Unnamed"));
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model };
        window.Show();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null);
        await model.History.HistoryCompletion;
        AppHarness.Layout(window);

        var rows = window.Descendant<ListBox>("HistoryList")!.GetRealizedContainers().ToArray();
        string[] Badges(Control row) => row.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("refbadge"))
            .Select(border => string.Join(" ", border.Classes.Where(name => name is "current" or "remote" or "tag" or "head").Append(
                border.GetVisualDescendants().OfType<TextBlock>().Single().Text!))).ToArray();
        Assert.Equal(["current main", "remote origin/main"], Badges(rows[0]));
        Assert.Equal(["topic", "tag v1.0"], Badges(rows[1]));
        Assert.Empty(Badges(rows[2]));
        window.Close();
    }

    // A tag's badge is as quiet as a remote-tracking branch's, unfilled and in the badges' own text, whatever the dialogs
    // call their own pills.
    [AvaloniaFact]
    public async Task WhenACommitCarriesATag_ShouldDrawItsBadgeLikeARemoteTrackingBranchs()
    {
        var h = new AppHarness();
        h.History.Commits[0] = h.History.Commits[0] with { Refs = [new("origin/main", CommitRefKind.RemoteBranch), new("v1.0", CommitRefKind.Tag)] };
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);

        var badges = window.Descendant<ListBox>("HistoryList")!.GetRealizedContainers().First().GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("refbadge")).ToArray();
        var (remote, tag) = (badges[0], badges[1]);
        TextBlock Text(Border badge) => badge.GetVisualDescendants().OfType<TextBlock>().Single();
        Assert.Equal(remote.Background, tag.Background);
        Assert.Equal((Text(remote).FontSize, Text(remote).FontWeight, Text(remote).Foreground), (Text(tag).FontSize, Text(tag).FontWeight, Text(tag).Foreground));
        window.Close();
    }

    // A tag looks as quiet as a remote-tracking branch, so its icon tells the two apart: a branch shows the branch glyph, a
    // remote-tracking branch the cloud, a tag the tag.
    [AvaloniaFact]
    public async Task WhenACommitCarriesBranchesAndTags_ShouldMarkEachBadgeWithTheIconOfItsKind()
    {
        var h = new AppHarness();
        h.History.Commits[0] = h.History.Commits[0] with
        {
            Refs = [new("main", CommitRefKind.CurrentBranch), new("topic", CommitRefKind.Branch), new("origin/main", CommitRefKind.RemoteBranch), new("v1.0", CommitRefKind.Tag)]
        };
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);

        var row = window.Descendant<ListBox>("HistoryList")!.GetRealizedContainers().First();
        var icons = row.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("refbadge"))
            .Select(border => string.Join(",", border.GetVisualDescendants().OfType<LineIcon>().Where(icon => icon.IsVisible).Select(icon => icon.Kind)));

        Assert.Equal(["branch", "branch", "cloud", "tag"], icons);
        window.Close();
    }

    // So the other headers stay over their columns.
    [AvaloniaFact]
    public async Task WhenAMergeAddsALane_ShouldWidenTheGraphHeaderToTheGraphOfThePage()
    {
        var h = new AppHarness();
        h.History.Commits.Clear();
        h.History.Commits.AddRange([Snapshots.Commit(Snapshots.Id('d'), "Merge", Snapshots.Id('b'), Snapshots.Id('c')),
            Snapshots.Commit(Snapshots.Id('b'), "Main", Snapshots.Id('a')), Snapshots.Commit(Snapshots.Id('c'), "Topic", Snapshots.Id('a')),
            Snapshots.Commit(Snapshots.Id('a'), "Root")]);
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model };
        window.Show();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null);
        await model.History.HistoryCompletion;
        AppHarness.Layout(window);

        var cell = window.Descendant<ListBox>("HistoryList")!.GetVisualDescendants().OfType<CommitGraphCell>().First();
        var header = window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Graph");
        Assert.Equal(2, model.History.Page.Graph.LaneCount);
        Assert.Equal(cell.Bounds.Width, header.MinWidth);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheCommitIsReplacedOrTheRepositoryReopens_ShouldBindTheCommitFilesWithoutLosingTheirSelection()
    {
        var h = new AppHarness();
        h.History.Details = id => new(Snapshots.Commit(id, id) with { Parents = [id + "-parent"] }, "body " + id,
            [new(Snapshots.Path("a.cs"), "M", "", "", "", ""), new(Snapshots.Path("b.cs"), "A", "", "", "", "")]);
        var reads = 0;
        h.History.Diff = (commit, file, _) =>
        {
            reads++;
            return new([file], [new('+', "+" + commit.Commit.Id + file.Path.Display)], DiffContentKind.Text, "");
        };
        h.Reader.Snapshot = Snapshots.Create(head: "head", identity: false);
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model };
        window.Show();
        for (var session = 0; session < 2; session++)
        {
            await model.Session.OpenRepositoryAsync("fixture");
            model.Navigator.ShowHistoryCommand.Execute(null);
            await model.History.HistoryCompletion;
            AppHarness.Layout(window);
            var list = window.Descendant<ListBox>("HistoryList")!;
            Assert.Same(model.History.Page.Commits, list.ItemsSource);
            for (var index = 0; index < 2; index++)
            {
                list.SelectedIndex = index;
                await model.History.Details.Completion;
                var panel = model.History.Details.Panel;
                panel.SelectedFile = panel.Files[1];
                await model.History.Details.Diff.Completion;
                AppHarness.Layout(window);
                var files = window.Descendant<ListBox>("CommitFilesList")!;
                Assert.Same(panel, files.DataContext);
                Assert.Same(panel.Files, files.ItemsSource);
                Assert.Same(panel.Files[1], files.SelectedItem);
                Assert.Same(panel.Files[1], panel.SelectedFile);
                Assert.Same(model.History.Details.Diff.Document.Presentation, window.Descendant<DiffView>("CommitDiffView")!.Document);
                Assert.Equal(session * 2 + index + 1, reads);
            }
            await model.CloseRepositoryCommand.ExecuteAsync(null);
            AppHarness.Layout(window);
            Assert.Empty(model.History.Page.Commits);
            Assert.Empty(model.History.Details.Diff.Document.Lines);
        }
        window.Close();
    }

}
