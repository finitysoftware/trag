using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Trag.Desktop.Controls.Diff;
using Trag.Domain.Diff;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.Desktop.UnitTests.Controls.Diff;

// The controlled refresh and scroll scenario: the real workspace, session, status and diff view models, the real
// window with its bindings, and a held status read followed by a held diff read. No arbitrary waits. The same file read
// again keeps its diff on screen meanwhile; another file shows nothing until it is read.
public sealed class DiffViewRefreshTests
{
    [AvaloniaTheory]
    [InlineData("none", false)]
    [InlineData("none", true)]
    [InlineData("same", false)]
    [InlineData("same", true)]
    [InlineData("changed", false)]
    [InlineData("changed", true)]
    [InlineData("different", false)]
    [InlineData("different", true)]
    public async Task WhenTheModeSwitchesAroundARefreshOrAnotherFile_ShouldKeepThePlaceOnlyForTheSameFile(string scenario, bool switchFirst)
    {
        var harness = new AppHarness();
        harness.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt"), Snapshots.Modified("b.txt")]);
        var changed = false;
        harness.Diff.Diff = (file, _, _) => Content(file.Path.Display, changed);
        await using var model = harness.Shell();
        var window = harness.Window(model);
        try
        {
            await model.Session.OpenRepositoryAsync("fixture");
            model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
            await model.WorkingDiff.Diff.Completion;
            AppHarness.Layout(window);
            var view = window.Descendant<DiffView>("FileDiffView")!;
            var rows = view.Descendant<ListBox>("Rows")!;
            var diff = model.WorkingDiff.Diff;
            Assert.Same(diff.Document.Presentation, view.Document);
            Assert.Equal(2_000, view.Document.Lines.Count);
            view.Mode = 0;
            AppHarness.Layout(window);
            rows.SelectedIndex = 801;
            rows.ScrollIntoView(rows.SelectedItem!);
            AppHarness.Layout(window);
            var anchor = view.TopSource;
            Assert.True(anchor > 100);

            if (switchFirst)
            {
                view.Mode = 1;
                AppHarness.Layout(window);
            }
            if (scenario != "none")
            {
                harness.Diff.Gate.IsClosed = true;
                if (scenario == "different")
                    model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[1];
                else
                {
                    changed = scenario == "changed";
                    harness.Reader.Gate.IsClosed = true;
                    var refresh = model.RefreshCommand.ExecuteAsync(null);
                    await harness.Reader.Gate.Arrival(2).WaitAsync(TimeSpan.FromSeconds(10));
                    harness.Reader.Gate.Open();
                    await refresh.WaitAsync(TimeSpan.FromSeconds(10));
                }
                await harness.Diff.Gate.Arrival(2).WaitAsync(TimeSpan.FromSeconds(10));
                AppHarness.Layout(window);
                if (scenario == "different")
                {
                    // While another file is read nothing is shown, and nothing is selected.
                    Assert.Empty(view.Document.Lines);
                    Assert.Equal(-1, view.SelectedSource);
                }
                else
                {
                    // While the same file is read again its diff stays, with the selected line.
                    Assert.Equal(2_000, view.Document.Lines.Count);
                    Assert.Equal(801, view.SelectedSource);
                }
            }
            if (!switchFirst)
            {
                view.Mode = 1;
                AppHarness.Layout(window);
            }
            harness.Diff.Gate.Open();
            await diff.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            AppHarness.Layout(window);

            Assert.Same(diff.Document.Presentation, view.Document);
            Assert.Equal(2_000, view.Document.Lines.Count);
            Assert.Equal(1, view.Mode);
            if (scenario == "changed") Assert.Equal("+a.txt changed last line", view.Document.Lines[^1].Text);
            if (scenario == "different")
            {
                Assert.Equal("b.txt", model.FileStatus.SelectedStatus!.PathText);
                Assert.Equal(-1, view.SelectedSource);
                Assert.Equal(0, view.TopSource);
            }
            else
            {
                // Line 801 and its neighbors are unchanged even in the 'changed' case, so the place is the same.
                Assert.Equal(801, view.SelectedSource);
                Assert.InRange(Math.Abs(view.TopSource - anchor), 0, 2);
            }
        }
        finally
        {
            harness.Reader.Gate.Open();
            harness.Diff.Gate.Open();
            window.Close();
        }
    }

    private static FileDiff Content(string path, bool changed)
    {
        var lines = new List<PatchLine>();
        for (var number = 1; number <= 1_000; number++)
        {
            lines.Add(new('-', "-" + path + " old " + number, number));
            lines.Add(new('+', changed && number == 1_000 ? "+" + path + " changed last line" : "+" + path + " new " + number, null, number));
        }
        return new([], lines, DiffContentKind.Text, "");
    }
}
