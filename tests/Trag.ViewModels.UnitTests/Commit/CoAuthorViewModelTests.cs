using Trag.Domain.History;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.Commit;

// Co-authors go into the commit message as Co-authored-by lines: the picker offers the repository's authors, most
// commits first and without the user, and whoever is typed as "Name <email>"; each co-author in the message can be
// taken out again.
public sealed class CoAuthorViewModelTests
{
    private static readonly GitIdentity Ana = new("Ana Anić", "ana@example.invalid");
    private static readonly GitIdentity Marko = new("Marko M.", "marko@example.invalid");

    [Fact]
    public async Task WhenACoAuthorIsChosen_ShouldWriteTheirLineIntoTheMessageAndListThem()
    {
        var h = new ShellHarness();
        h.History.Authors.AddRange([Marko, Ana, Snapshots.Identity, Ana]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Commit.CommitMessage = "Fix the parser";

        await model.Commit.CoAuthorPicker.OpenCommand.ExecuteAsync(null);
        Assert.Equal([Ana, Marko], model.Commit.CoAuthorPicker.Choices);
        model.Commit.CoAuthorPicker.IsOpen = true;
        model.Commit.CoAuthorPicker.ChooseCommand.Execute(Ana);

        Assert.Equal("Fix the parser\n\nCo-authored-by: Ana Anić <ana@example.invalid>", model.Commit.CommitMessage);
        Assert.Equal([Ana], model.Commit.CoAuthorList);
        Assert.False(model.Commit.CoAuthorPicker.IsOpen);
    }

    [Fact]
    public async Task WhenTheSearchIsTyped_ShouldNarrowTheAuthorsOrOfferTheTypedIdentity()
    {
        var h = new ShellHarness();
        h.History.Authors.AddRange([Marko, Ana]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var picker = model.Commit.CoAuthorPicker;
        await picker.OpenCommand.ExecuteAsync(null);

        picker.Search = "anić";
        Assert.Equal([Ana], picker.Choices);
        picker.Search = "Iva <iva@example.invalid>";
        Assert.Equal([new GitIdentity("Iva", "iva@example.invalid")], picker.Choices);
        picker.ChooseCommand.Execute(picker.Choices[0]);
        Assert.Equal("\n\nCo-authored-by: Iva <iva@example.invalid>", model.Commit.CommitMessage);
        Assert.Equal("Write the commit's subject above its Co-authored-by lines.", model.Commit.MessageHint);
        model.Commit.CommitMessage = "Fix" + model.Commit.CommitMessage;
        Assert.Equal("", model.Commit.MessageHint);

        await picker.OpenCommand.ExecuteAsync(null);
        Assert.Equal(("", 2), (picker.Search, picker.Choices.Count));
    }

    // Before the first commit there is nobody to read, and Git would fail on the missing HEAD; a typed identity still is.
    [Fact]
    public async Task WhenTheRepositoryHasNoCommitYet_ShouldOfferNoAuthorsButStillATypedIdentity()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(unborn: true);
        h.History.Authors.Add(Ana);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var picker = model.Commit.CoAuthorPicker;

        await picker.OpenCommand.ExecuteAsync(null);

        Assert.Equal((0, ""), (picker.Choices.Count, picker.People.Message));
        picker.Search = "Iva <iva@example.invalid>";
        Assert.Single(picker.Choices);
    }

    [Fact]
    public async Task WhenACoAuthorIsRemoved_ShouldTakeTheirLineOutOfTheMessage()
    {
        var h = new ShellHarness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Commit.CommitMessage = "Fix\n\nCo-authored-by: Ana Anić <ana@example.invalid>\nCo-authored-by: Marko M. <marko@example.invalid>";
        var changed = new List<string?>();
        model.Commit.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.Commit.RemoveCoAuthorCommand.Execute(Ana);

        Assert.Equal("Fix\n\nCo-authored-by: Marko M. <marko@example.invalid>", model.Commit.CommitMessage);
        Assert.Equal([Marko], model.Commit.CoAuthorList);
        Assert.Contains(nameof(model.Commit.CoAuthorList), changed);
    }

    // While amending, the co-author goes into the message of the commit being replaced.
    [Fact]
    public async Task WhenAmending_ShouldAddTheCoAuthorToTheMessageThatReplacesTheLastCommit()
    {
        var h = new ShellHarness();
        h.History.Authors.Add(Ana);
        h.History.Details = id => new CommitDetails(Snapshots.Commit(id, "Last"), "Last", []);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Commit.IsAmending = true; await model.Commit.AmendCompletion;

        await model.Commit.CoAuthorPicker.OpenCommand.ExecuteAsync(null);
        model.Commit.CoAuthorPicker.ChooseCommand.Execute(Ana);
        await model.Commit.CommitCommand.ExecuteAsync(null);

        var request = Assert.IsType<CommitRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal((true, "Last\n\nCo-authored-by: Ana Anić <ana@example.invalid>"), (request.Amend, request.Message));
    }
}
