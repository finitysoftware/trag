using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Trag.Application.Diff;
using Trag.Desktop.Controls.Diff;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Diff;

namespace Trag.Desktop.UnitTests.Controls.Diff;

public sealed class DiffNoticeTests
{
    // A diff too large to show says so with a button that loads the limited preview; once the preview is shown, or for a
    // diff that has none, the button is gone.
    [AvaloniaFact]
    public async Task WhenTheDiffCanBePreviewed_ShouldOfferTheButtonThatLoadsThePreview()
    {
        var calls = new List<bool>();
        var model = new DiffViewModel(new DiffReader(new BackgroundDiffBuilder(), new(), DiffMessages.Working));
        var window = new Window { Content = new DiffNotice { DataContext = model }, Width = 600, Height = 200 };
        window.Show();
        model.Start(new DiffSource("large.cs", "working tree"), (preview, _) =>
        {
            calls.Add(preview);
            return Task.FromResult<FileDiff?>(preview ? new([], [new('+', "+preview")], DiffContentKind.Limited, "Limited")
                : new([], [], DiffContentKind.Limited, "Large", CanPreview: true));
        });
        await model.Completion;
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var button = window.GetVisualDescendants().OfType<Button>().Single(item => Equals(item.Content, "Load limited preview"));
        var offered = button.IsEffectivelyVisible;

        Assert.True(button.Focus());
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await model.LoadPreviewCommand.ExecutionTask!;
        await model.Completion;
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();

        Assert.Equal((true, false), (offered, button.IsEffectivelyVisible));
        Assert.Equal([false, true], calls);
        Assert.Equal("+preview", Assert.Single(model.Document.Lines).Text);
        window.Close();
    }
}
