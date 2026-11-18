using Trag.Domain;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

public sealed class TagTests
{
    // Without a message the tag is a lightweight one: the ref names the commit itself.
    [Fact]
    public async Task WhenATagWithoutAMessageIsCreatedAtAnOlderCommit_ShouldPointALightweightTagThereAndLeaveHeadAlone()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (first, head) = await TwoCommits(fixture);
        var snapshot = await fixture.ReadAsync();

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new TagRequest(snapshot, new("v1.0", first)), fixture.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(TagOutcomeText.Created("v1.0", GitObjectId.Short(first)), result.Message);
        Assert.Equal(first, await fixture.GitAsync("rev-parse", "refs/tags/v1.0"));
        Assert.Equal("commit", await fixture.GitAsync("cat-file", "-t", "refs/tags/v1.0"));
        Assert.Equal(head, await fixture.GitAsync("rev-parse", "HEAD"));
        Assert.Equal("main", (await fixture.ReadAsync()).Branch);
    }

    // With a message it is an annotated tag that keeps the message as it was typed, a line Git would take for a comment
    // included.
    [Fact]
    public async Task WhenATagWithAMessageIsCreated_ShouldKeepAnAnnotatedTagWithThatMessageAtTheCommit()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (_, head) = await TwoCommits(fixture);
        var snapshot = await fixture.ReadAsync();
        var message = "Second release\n\n# not a comment\nWith notes.";

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new TagRequest(snapshot, new("release/2.0", head, message)), fixture.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("tag", await fixture.GitAsync("cat-file", "-t", "refs/tags/release/2.0"));
        Assert.Equal(head, await fixture.GitAsync("rev-parse", "refs/tags/release/2.0^{commit}"));
        Assert.Equal(message, await fixture.GitAsync("tag", "--list", "--no-column", "--format=%(contents:subject)%0a%0a%(contents:body)", "release/2.0"));
    }

    // A message of nothing but whitespace is no message: the tag is a lightweight one.
    [Fact]
    public async Task WhenAnAnnotatedTagMessageIsOnlyWhitespace_ShouldMakeALightweightTag()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (_, head) = await TwoCommits(fixture);

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new TagRequest(await fixture.ReadAsync(), new("blank", head, "  \n\t \n")), fixture.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("commit", await fixture.GitAsync("cat-file", "-t", "refs/tags/blank"));
    }

    [Fact]
    public async Task WhenATagNameIsInvalidTakenOrItsCommitIsGone_ShouldRefuseItAndCreateNothing()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (first, head) = await TwoCommits(fixture);
        await fixture.GitAsync("tag", "v1.0", first);
        var snapshot = await fixture.ReadAsync();
        var writer = new GitWriteRouter(fixture.Runner);

        var invalid = await writer.ExecuteAsync(new TagRequest(snapshot, new("bad..name", head)), fixture.Token);
        var taken = await writer.ExecuteAsync(new TagRequest(snapshot, new("v1.0", head)), fixture.Token);
        var gone = await writer.ExecuteAsync(new TagRequest(snapshot, new("v3.0", new string('e', 40))), fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, TagOutcomeText.NameInvalid), (invalid.Outcome, invalid.Message));
        Assert.Equal((GitWriteOutcome.Rejected, TagOutcomeText.NameTaken("v1.0")), (taken.Outcome, taken.Message));
        Assert.Equal((GitWriteOutcome.Rejected, HistoryText.CommitGone), (gone.Outcome, gone.Message));
        Assert.Equal("v1.0", await fixture.GitAsync("tag", "--list"));
        Assert.Equal(first, await fixture.GitAsync("rev-parse", "refs/tags/v1.0"));
    }

    // tag.gpgSign turned on after Trag read the repository (a global setting it does not watch): the request still asks
    // for a lightweight tag, Git wants a message for the signed one, and no editor may open for it.
    [Fact]
    public async Task WhenTagsBecameSignedAfterTheRepositoryWasRead_ShouldRefuseATagWithoutAMessageWithoutAnEditor()
    {
        using var fixture = await GitFixture.CreateAsync("main", TimeSpan.FromSeconds(30));
        var (_, head) = await TwoCommits(fixture);
        var snapshot = await fixture.ReadAsync();
        await fixture.GitAsync("config", "tag.gpgSign", "true");

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new TagRequest(snapshot, new("v1.0", head)), fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, TagOutcomeText.NeedsMessage), (result.Outcome, result.Message));
        Assert.Equal("", await fixture.GitAsync("tag", "--list"));
    }

    // Deleting a tag takes only the name away; the commit stays where it was. A tag that is gone already is refused.
    [Fact]
    public async Task WhenATagIsDeleted_ShouldRemoveOnlyTheTagAndRefuseOneThatIsGone()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (first, head) = await TwoCommits(fixture);
        await fixture.GitAsync("tag", "--annotate", "--message=Release", "v1.0", first);
        var snapshot = await fixture.ReadAsync();
        var writer = new GitWriteRouter(fixture.Runner);

        var deleted = await writer.ExecuteAsync(new TagDeleteRequest(snapshot, "v1.0"), fixture.Token);
        var gone = await writer.ExecuteAsync(new TagDeleteRequest(snapshot, "v1.0"), fixture.Token);

        Assert.True(deleted.Outcome == GitWriteOutcome.Succeeded, deleted.Message + " " + deleted.Details);
        Assert.Equal(TagOutcomeText.Deleted("v1.0", GitObjectId.Short(first)), deleted.Message);
        Assert.Equal((GitWriteOutcome.Rejected, TagOutcomeText.Gone("v1.0")), (gone.Outcome, gone.Message));
        Assert.Equal("", await fixture.GitAsync("tag", "--list"));
        Assert.Equal("commit", await fixture.GitAsync("cat-file", "-t", first));
        Assert.Equal(head, await fixture.GitAsync("rev-parse", "HEAD"));
    }

    // A name with a slash, as release/1.0, is one tag under refs/tags, created and deleted by its whole name.
    [Fact]
    public async Task WhenATagNameHasASlash_ShouldCreateAndDeleteIt()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (first, _) = await TwoCommits(fixture);
        var writer = new GitWriteRouter(fixture.Runner);

        var created = await writer.ExecuteAsync(new TagRequest(await fixture.ReadAsync(), new("release/1.0", first)), fixture.Token);
        Assert.True(created.Outcome == GitWriteOutcome.Succeeded, created.Message + " " + created.Details);
        Assert.Equal(first, await fixture.GitAsync("rev-parse", "refs/tags/release/1.0"));
        var deleted = await writer.ExecuteAsync(new TagDeleteRequest(await fixture.ReadAsync(), "release/1.0"), fixture.Token);

        Assert.True(deleted.Outcome == GitWriteOutcome.Succeeded, deleted.Message + " " + deleted.Details);
        Assert.Equal("", await fixture.GitAsync("tag", "--list"));
    }

    // git tag refuses to create a name that starts with '-', but another program can (git update-ref), and the list shows
    // it like any tag, so deleting it has to hand Git the name where it cannot be read as an option.
    [Fact]
    public async Task WhenATagNamedDashXWasCreatedOutside_ShouldDeleteIt()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (first, _) = await TwoCommits(fixture);
        await fixture.GitAsync("update-ref", "refs/tags/-x", first);

        var deleted = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new TagDeleteRequest(await fixture.ReadAsync(), "-x"), fixture.Token);

        Assert.True(deleted.Outcome == GitWriteOutcome.Succeeded, deleted.Message + " " + deleted.Details);
        Assert.Equal(TagOutcomeText.Deleted("-x", GitObjectId.Short(first)), deleted.Message);
        Assert.Equal("", await fixture.GitAsync("tag", "--list"));
    }

    // The history shows tags on their commits, so the snapshot it follows has to change when one comes or goes.
    [Fact]
    public async Task WhenATagIsCreatedOrDeleted_ShouldChangeTheSnapshotsTagFingerprintAndNothingTheBranchesFollow()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (first, _) = await TwoCommits(fixture);
        var before = await fixture.ReadAsync();

        await fixture.GitAsync("tag", "v1.0", first);
        var tagged = await fixture.ReadAsync();
        await fixture.GitAsync("tag", "--delete", "v1.0");
        var untagged = await fixture.ReadAsync();

        Assert.Equal("", before.TagRefs);
        Assert.NotEqual("", tagged.TagRefs);
        Assert.Equal("", untagged.TagRefs);
        Assert.Equal(before.BranchesKey, tagged.BranchesKey);
        Assert.Equal(before.RemoteRefs, tagged.RemoteRefs);
    }

    // Answers the first commit and HEAD, the second one, on main.
    private static async Task<(string First, string Head)> TwoCommits(GitFixture fixture)
    {
        await fixture.WriteAsync("readme.txt", "first\n");
        var first = await fixture.CommitAsync("First", "readme.txt");
        await fixture.WriteAsync("readme.txt", "second\n");
        return (first, await fixture.CommitAsync("Second", "readme.txt"));
    }
}
