using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

// The file status stays alive while the merge view shows, so its banner and headings change while hidden; coming back,
// they must be drawn with the new counts, not left empty.
public sealed class FileStatusDuringMergeTests
{
    private static RepositorySnapshot Merging(int conflicts, int staged) => Snapshots.Create(operation: RepositoryOperation.Merge,
        files: [.. Enumerable.Range(0, conflicts).Select(i => Snapshots.Conflict($"c{i}.txt")), .. Enumerable.Range(0, staged).Select(i => Snapshots.Staged($"s{i}.txt"))],
        merge: new(Snapshots.Id('b'), Snapshots.HeadId, "Merge branch 'feature' into main", "fp", ""));

    [AvaloniaFact]
    public async Task WhenTheCountsChangeWhileTheMergeViewShows_ShouldDrawTheBannerAndHeadingsWithTheNewCounts()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Merging(2, 0);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowFileStatusCommand.Execute(null); AppHarness.Layout(window);
        Assert.StartsWith("Merge in progress: 2 files are still in conflict.", Shown(window.Descendant<Border>("MergeConflictsBanner")!));

        model.Navigator.ShowMergeCommand.Execute(null); AppHarness.Layout(window);
        h.Reader.Snapshot = Merging(1, 1);
        await model.RefreshCommand.ExecuteAsync(null); AppHarness.Layout(window);
        model.Navigator.ShowFileStatusCommand.Execute(null); AppHarness.Layout(window);

        Assert.StartsWith("Merge in progress: 1 file is still in conflict.", Shown(window.Descendant<Border>("MergeConflictsBanner")!));
        Assert.Equal("Conflicts · 1", Shown(window.Descendant<Control>("ConflictHeading")!));
        Assert.Equal("Staged files · 1", Shown(window.Descendant<Control>("StagedHeading")!));
        Assert.Equal("Unstaged files · 0", Shown(window.Descendant<Control>("UnstagedHeading")!));
        window.Close();
    }

    // The text a control's text blocks actually lay out: a block whose layout went stale lays out less than its text.
    private static string Shown(Control control) => string.Join(" ",
        (control is TextBlock self ? [self] : control.GetVisualDescendants().OfType<TextBlock>()).Select(Shown));

    private static string Shown(TextBlock block)
    {
        var text = (block.Inlines is { Count: > 0 } inlines ? inlines.Text : block.Text) ?? "";
        return text[..Math.Min(text.Length, block.TextLayout.TextLines.Sum(line => line.Length))];
    }
}
