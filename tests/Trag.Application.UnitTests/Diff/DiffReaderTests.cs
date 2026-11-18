using Trag.Application.Diff;
using Trag.Application.Live;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;

namespace Trag.Application.UnitTests.Diff;

// The rules of reading a pane's diff that the pane's own tests do not reach: a refresh whose read fails keeps the shown
// diff, and a refresh of another source is a fresh start that shows its loading.
public sealed class DiffReaderTests
{
    private static DiffSource Source(string path) => new(path, "working tree");
    private static FileDiff File(string text) => new([], [new('+', "+" + text)], DiffContentKind.Text, "");

    [Fact]
    public async Task WhenARefreshReadFails_ShouldKeepTheShownDiffAndStopBeingBusy()
    {
        var reader = await ShowingAAsync();
        var shown = reader.Document;

        reader.Refresh(Source("a.cs"), (_, _) => Task.FromException<FileDiff?>(new IOException("the file is locked")));
        await reader.Completion;
        Assert.Same(shown, reader.Document);
        Assert.False(reader.IsBusy);
    }

    // The shell's Cancel reaches a refresh too, which only confirms the diff on screen: interrupting it keeps that diff.
    [Fact]
    public async Task WhenARefreshIsCancelled_ShouldKeepTheShownDiff()
    {
        var cancels = new CancelRegistry();
        var reader = await ShowingAAsync(cancels);
        var shown = reader.Document;

        reader.Refresh(Source("a.cs"), async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        });
        cancels.CancelAll();
        await reader.Completion;
        Assert.Same(shown, reader.Document);
    }

    [Fact]
    public async Task WhenARefreshOfAnotherSourceIsStillReading_ShouldShowItsLoading()
    {
        var reader = await ShowingAAsync();

        var held = new TaskCompletionSource<FileDiff?>();
        reader.Refresh(Source("b.cs"), (_, _) => held.Task);
        Assert.Equal((DiffMessages.Working.Loading, "b.cs"), (reader.Document.Message, reader.Document.Presentation.Source?.Path));
        Assert.True(reader.IsBusy);
    }

    [Fact]
    public async Task WhenARefreshOfAnotherSourceFinishesReading_ShouldShowItsDiff()
    {
        var reader = await ShowingAAsync();

        var held = new TaskCompletionSource<FileDiff?>();
        reader.Refresh(Source("b.cs"), (_, _) => held.Task);
        held.SetResult(File("b"));
        await reader.Completion;
        Assert.Equal("+b", Assert.Single(reader.Document.Lines).Text);
    }

    // A reader that has finished showing a.cs's working diff.
    private static async Task<DiffReader> ShowingAAsync(CancelRegistry? cancels = null)
    {
        var reader = new DiffReader(new BackgroundDiffBuilder(), cancels ?? new CancelRegistry(), DiffMessages.Working);
        reader.Start(Source("a.cs"), (_, _) => Task.FromResult<FileDiff?>(File("a")));
        await reader.Completion;
        return reader;
    }
}
