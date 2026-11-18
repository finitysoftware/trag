using Trag.Application.Diff;
using Trag.Application.Live;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;
using Trag.ViewModels.Diff;

namespace Trag.ViewModels.UnitTests.Diff;

public sealed class DiffViewModelTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WhenTheUserCancelsAReadOrPresentationThatIgnoresIt_ShouldShowTheCancelledMessageForItsSource(bool inPresentation, bool commit)
    {
        var cancels = new CancelRegistry();
        var held = new AsyncGate { IsClosed = true };
        var builder = new HeldBuilder(inPresentation ? held : null);
        var messages = commit ? DiffMessages.Commit : DiffMessages.Working;
        var model = new DiffViewModel(new DiffReader(builder, cancels, messages));
        model.Start(Source("file.cs"), async (_, ct) =>
        {
            if (!inPresentation) await held.WaitAsync(ct);
            return File("old");
        });
        await held.Started;
        Assert.True(cancels.CanCancel);
        cancels.CancelAll();
        Assert.True(held.Token.IsCancellationRequested);
        held.Open();
        await model.Completion;

        Assert.False(model.IsBusy);
        Assert.False(cancels.CanCancel);
        Assert.Equal(messages.Cancelled, model.Document.Message);
        Assert.Empty(model.Document.Lines);
        Assert.Equal(Source("file.cs"), model.Document.Presentation.Source);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WhenASupersededReadSucceedsOrFailsLate_ShouldKeepTheNewDocument(bool inPresentation, bool fail)
    {
        var held = new AsyncGate { IsClosed = true };
        var model = new DiffViewModel(new DiffReader(new HeldBuilder(inPresentation ? held : null), new(), DiffMessages.Working));
        model.Start(Source("old.cs"), async (_, ct) =>
        {
            if (!inPresentation) await held.WaitAsync(ct);
            return File("old");
        });
        var old = model.Completion;
        await held.Started;
        held.IsClosed = false;
        model.Start(Source("current.cs"), (_, _) => Task.FromResult<FileDiff?>(File("current")));
        await model.Completion;
        var current = model.Document;
        if (fail) held.Fail(new IOException("late failure"));
        else held.Open();
        await old;

        Assert.Same(current, model.Document);
        Assert.Equal("+current", Assert.Single(current.Presentation.Lines).Text);
        Assert.False(model.IsBusy);
    }

    [Theory]
    [InlineData(DiffContentKind.Text)]
    [InlineData(DiffContentKind.Binary)]
    [InlineData(DiffContentKind.Unavailable)]
    public async Task WhenAReadReturnsNoLines_ShouldShowItsMessageAndOfferNoPreview(DiffContentKind kind)
    {
        var model = new DiffViewModel(new DiffReader(new BackgroundDiffBuilder(), new(), DiffMessages.Commit));
        model.Start(Source("file"), (_, _) => Task.FromResult<FileDiff?>(new([], [], kind, "Result notice")));
        await model.Completion;
        Assert.Empty(model.Document.Lines);
        Assert.Empty(model.Document.Presentation.Lines);
        Assert.Equal("Result notice", model.Document.Message);
        Assert.False(model.LoadPreviewCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenALimitedDiffIsPreviewedAndThenCleared_ShouldReadTheSameSourceAndThenOfferNoPreview()
    {
        var calls = new List<bool>();
        var model = new DiffViewModel(new DiffReader(new BackgroundDiffBuilder(), new(), DiffMessages.Working));
        model.Start(Source("large.cs"), (preview, _) =>
        {
            calls.Add(preview);
            return Task.FromResult<FileDiff?>(File(preview ? "preview" : "limited") with { CanPreview = !preview });
        });
        await model.Completion;
        Assert.True(model.LoadPreviewCommand.CanExecute(null));
        await model.LoadPreviewCommand.ExecuteAsync(null);
        Assert.Equal([false, true], calls);
        Assert.Equal("+preview", Assert.Single(model.Document.Lines).Text);
        model.Clear();
        Assert.False(model.LoadPreviewCommand.CanExecute(null));
        Assert.Empty(model.Document.Lines);
        Assert.Equal(DiffMessages.Working.Empty, model.Document.Message);
    }

    [Fact]
    public async Task WhenAReadFindsTheRepositoryChanged_ShouldShowNoPreviousContent()
    {
        var model = new DiffViewModel(new DiffReader(new BackgroundDiffBuilder(), new(), DiffMessages.Commit));
        model.Start(Source("file"), (_, _) => Task.FromResult<FileDiff?>(File("first")));
        await model.Completion;
        model.Start(Source("file"), (_, _) => Task.FromResult<FileDiff?>(null));
        await model.Completion;
        Assert.Empty(model.Document.Lines);
        Assert.Equal(DiffMessages.Commit.Changed, model.Document.Message);
    }

    // The view keeps its place only across documents of one source, so every document of a read names it, the loading
    // one and the message ones included.
    [Fact]
    public async Task WhenAReadLoadsAndIsThenCleared_ShouldNameItsSourceInEveryDocumentUntilTheClear()
    {
        var held = new AsyncGate { IsClosed = true };
        var model = new DiffViewModel(new DiffReader(new BackgroundDiffBuilder(), new(), DiffMessages.Working));
        model.Start(Source("file.cs"), async (_, ct) =>
        {
            await held.WaitAsync(ct);
            return File("content");
        });
        await held.Started;
        Assert.Equal(DiffMessages.Working.Loading, model.Document.Message);
        Assert.Equal(Source("file.cs"), model.Document.Presentation.Source);
        Assert.Empty(model.Document.Lines);
        held.Open();
        await model.Completion;
        Assert.Equal(Source("file.cs"), model.Document.Presentation.Source);
        Assert.Equal("+content", Assert.Single(model.Document.Lines).Text);
        model.Clear();
        Assert.Same(DiffPresentation.Empty, model.Document.Presentation);
    }

    private static DiffSource Source(string path) => new(path, "working tree");
    private static FileDiff File(string text) => new([], [new('+', "+" + text)], DiffContentKind.Text, "");

    [Fact]
    public async Task WhenAnOldPreviewFinishesAfterANewFileWasSelected_ShouldKeepTheNewFilesPreview()
    {
        var held = new AsyncGate { IsClosed = true };
        var model = new DiffViewModel(new DiffReader(new BackgroundDiffBuilder(), new(), DiffMessages.Working));
        model.Start(Source("old.cs"), async (preview, ct) =>
        {
            if (preview) await held.WaitAsync(ct);
            return File("old") with { CanPreview = !preview };
        });
        await model.Completion;
        var oldPreview = model.LoadPreviewCommand.ExecuteAsync(null);
        await held.Started;

        model.Start(Source("current.cs"), (preview, _) => Task.FromResult<FileDiff?>(File("current") with { CanPreview = !preview }));
        await model.Completion;
        Assert.True(model.LoadPreviewCommand.CanExecute(null));
        await model.LoadPreviewCommand.ExecuteAsync(null);
        var current = model.Document;
        held.Open();
        await oldPreview;
        Assert.Same(current, model.Document);
        Assert.Equal("+current", Assert.Single(current.Lines).Text);
        Assert.False(current.CanPreview);
    }

    private sealed class HeldBuilder(AsyncGate? held) : IDiffPresentationBuilder
    {
        public async Task<DiffPresentation> BuildAsync(FileDiff diff, string path, CancellationToken ct)
        {
            if (held is not null) await held.WaitAsync(ct);
            // Deliberately ignores cancellation, as a third-party builder might.
            return await new BackgroundDiffBuilder().BuildAsync(diff, path, CancellationToken.None);
        }
    }
}
