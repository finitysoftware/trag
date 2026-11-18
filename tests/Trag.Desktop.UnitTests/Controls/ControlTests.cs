using System.Buffers.Binary;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Trag.Desktop.Controls;
using Trag.Desktop.Platform;
using Trag.Desktop.Views;
using Trag.Domain.Presentation;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Controls;

public sealed class ControlTests
{
    // A recycled row gets its new text before its new tokens: the old tokens that no longer fit the text are left out
    // until the new ones arrive, instead of cutting the text where it has none.
    [AvaloniaFact]
    public void WhenTheCodeChangesBeforeItsTokens_ShouldShowTheNewCodeWithoutTheOldTokens()
    {
        var line = new CodeTextBlock { Code = "public void Run()", Tokens = [new(0, 6, SyntaxKind.Keyword), new(7, 4, SyntaxKind.Keyword)] };

        line.Code = "x = 1";
        line.Tokens = [new(4, 1, SyntaxKind.Number)];

        Assert.Equal("x = 1", string.Concat(line.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text)));
    }

    [AvaloniaFact]
    public async Task WhenTheUserMovesThePaneSplitterByKeysAndDoubleTapsIt_ShouldNudgeJumpToTheEdgesAndRestoreTheDefault()
    {
        var h = new AppHarness();
        await using var model = h.Shell(); model.Repository.Apply(Snapshots.Create());
        var window = h.Window(model);
        var pane = window.Descendant<RatioGrid>("StatusFilePanels")!;
        var handle = window.Descendant<PaneSplitter>("StatusFilesDivider")!;
        handle.Focus();
        var before = pane.EffectiveRatio;
        var size = pane.Bounds.Width - RatioGrid.SplitterThickness;
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null); AppHarness.Layout(window);
        Assert.InRange(model.Layout.DiffFilesRatio - before, 16 / size - 0.001, 16 / size + 0.001);
        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null); AppHarness.Layout(window);
        Assert.InRange(model.Layout.DiffFilesRatio, before - 0.001, before + 0.001);
        window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null); AppHarness.Layout(window);
        Assert.Equal(0, model.Layout.DiffFilesRatio); Assert.InRange(pane.Children[0].Bounds.Width, 0, 1);
        window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null); AppHarness.Layout(window);
        Assert.Equal(1, model.Layout.DiffFilesRatio); Assert.InRange(pane.Children[2].Bounds.Width, 0, 1);
        window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null); AppHarness.Layout(window);
        Assert.Equal(1, model.Layout.DiffFilesRatio); // the other axis is ignored
        var centre = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left); window.MouseUp(centre, MouseButton.Left);
        window.MouseDown(centre, MouseButton.Left); window.MouseUp(centre, MouseButton.Left); AppHarness.Layout(window);
        Assert.Equal(pane.DefaultRatio, model.Layout.DiffFilesRatio);
        window.Close();
    }

    [AvaloniaFact]
    public void WhenTheXamlUsesAnIconKind_ShouldFindItAmongTheLineIconKinds()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Trag.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var used = Directory.EnumerateFiles(Path.Combine(root!.FullName, "src", "Trag.Desktop"), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "Kind=\"([a-z]+)\"").Select(match => match.Groups[1].Value)).Distinct().ToArray();
        Assert.NotEmpty(used);
        Assert.All(used, kind => Assert.Contains(kind, LineIcon.Kinds));
    }

    [AvaloniaFact]
    public void WhenAnIconKindIsUnknown_ShouldStillRender()
    {
        var window = new Window { Content = new LineIcon { Kind = "no-such-icon", Width = 24, Height = 24 } };
        window.Show(); AppHarness.Layout(window);
        Assert.Equal("no-such-icon", ((LineIcon)window.Content!).Kind);
        window.Close();
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(20, 32)]
    [InlineData(32, 32)]
    [InlineData(300, 256)]
    [InlineData(1, 16)]
    public void WhenTheTitleBarAsksForAnIconSize_ShouldPickTheSmallestEntryAtLeastAsLargeElseTheLargest(int wanted, int expectedSize)
    {
        int[] sizes = [32, 16, 256];
        var ico = new byte[6 + sizes.Length * 16 + sizes.Length * 4];
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(4), (ushort)sizes.Length);
        for (var i = 0; i < sizes.Length; i++)
        {
            var entry = ico.AsSpan(6 + i * 16, 16);
            entry[0] = entry[1] = (byte)(sizes[i] == 256 ? 0 : sizes[i]);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], 4);
            var offset = 6 + sizes.Length * 16 + i * 4;
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);
            BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(offset), sizes[i]);
        }
        var image = TitleBarIcon.PickEntry(ico, wanted);
        Assert.Equal(4, image.Length);
        Assert.Equal(expectedSize, BinaryPrimitives.ReadInt32LittleEndian(image));
    }

    // A headless window has no folder dialog: the host refuses, and the catalog says the picker failed.
    [AvaloniaFact]
    public async Task WhenTheHostHasNoFolderDialog_ShouldRefuseToPick()
    {
        await using var model = new ShellHarness().Shell();
        var window = new MainWindow { DataContext = model }; window.Show();
        await Assert.ThrowsAsync<NotSupportedException>(() => model.Host!.PickFolderAsync());
        window.Close();
    }

    // The list keeps scrolling to its selection: a refresh that leaves the selection alone ends on the selected row, while
    // ShellTests.WhenARefreshAppliesADelta_ShouldKeepTheSelectionAndTheScrolledViewport covers the anchored viewport.
    [AvaloniaFact]
    public async Task WhenTheRepositoryClosesAndOpensAgain_ShouldLetTheFileListReleaseTheOldRowsAndShowTheNewOnes()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: Enumerable.Range(0, 200).Select(i => Snapshots.Modified($"file-{i:D5}")).ToArray());
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var window = h.Window(model);
        var list = (StatusListBox)window.Descendant<ListBox>("UnstagedList")!;
        Assert.Equal(200, list.ItemCount);
        var (previous, previousFiles) = (model.FileStatus.UnstagedFiles, model.Repository.Status.Unstaged);
        await model.CloseRepositoryCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Assert.NotSame(previous, model.FileStatus.UnstagedFiles); Assert.Equal(0, list.ItemCount);
        previousFiles.ApplyBatch([]); // the detached list no longer drives the control
        Assert.True(list.AutoScrollToSelectedItem);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        Assert.Equal(200, list.ItemCount);
        window.Close();
    }
}
