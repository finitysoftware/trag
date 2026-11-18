using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Writes;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.UnitTests.Blocks;

public sealed partial class FormTests
{
    private sealed partial class Draft : Form
    {
        [ObservableProperty] public partial string Text { get; set; } = "";
        protected override void ResetFields() => Text = "";
    }

    [Fact]
    public void WhenAFormOpensAndCloses_ShouldStartCleanAndKeepTheTypedInputUntilReset()
    {
        var draft = new Draft { Text = "leftover", Notice = "old" };
        draft.Open();
        Assert.True(draft.IsOpen); Assert.Equal("", draft.Text); Assert.Equal("", draft.Notice); Assert.False(draft.HasNotice);
        draft.Text = "typed"; draft.Notice = "hint";
        draft.Close();
        Assert.False(draft.IsOpen); Assert.Equal("typed", draft.Text); Assert.Equal("", draft.Notice); // a closed form forgets its notice, not its input
        draft.Reset();
        Assert.Equal("", draft.Text);
    }

    [Fact]
    public async Task WhenAFormIsSubmitted_ShouldCloseAndClearOnSuccessAndStayOpenWithTheReasonOtherwise()
    {
        var draft = new Draft();
        draft.Open(); draft.Text = "message";
        var rejected = await draft.SubmitAsync(() => Task.FromResult<GitWriteResult?>(new(GitWriteOutcome.Rejected, "Index locked.")), "Could not stash.");
        Assert.Equal(GitWriteOutcome.Rejected, rejected!.Outcome); Assert.True(draft.IsOpen); Assert.Equal("message", draft.Text); Assert.Equal("Index locked.", draft.Notice);
        Assert.Null(await draft.SubmitAsync(() => Task.FromResult<GitWriteResult?>(null), "Could not stash."));
        Assert.True(draft.IsOpen); Assert.Equal("Could not stash.", draft.Notice);
        var done = await draft.SubmitAsync(() => Task.FromResult<GitWriteResult?>(new(GitWriteOutcome.Succeeded, "Stashed.")), "Could not stash.");
        Assert.Equal(GitWriteOutcome.Succeeded, done!.Outcome); Assert.False(draft.IsOpen); Assert.Equal("", draft.Text); Assert.Equal("", draft.Notice);
    }

    // A write left at conflicts has started: its view takes over, so the form is done too.
    [Fact]
    public async Task WhenTheWriteStopsAtConflicts_ShouldCloseAndClearTheForm()
    {
        var draft = new Draft();
        draft.Open(); draft.Text = "message";
        await draft.SubmitAsync(() => Task.FromResult<GitWriteResult?>(new(GitWriteOutcome.NeedsResolution, "Resolve the conflicts.")), "Could not merge.");
        Assert.False(draft.IsOpen); Assert.Equal("", draft.Text); Assert.Equal("", draft.Notice);
    }
}
