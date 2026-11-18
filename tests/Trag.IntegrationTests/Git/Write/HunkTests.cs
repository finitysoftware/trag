using System.Text;
using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// One hunk of a file staged, unstaged or discarded alone, from the diff Trag shows: the other hunk stays as it was, the
// working tree is kept by a stage or unstage and the index by a discard, and a patch that no longer fits changes nothing.
public sealed class HunkTests
{
    private const string Before = "a\nb\nc\nd\ne\nf\ng\nh\ni\n";
    private const string After = "a\nb\nc\nd\nE\nf\ng\nh\nI\n";

    [Fact]
    public async Task WhenOneOfTwoHunksIsStaged_ShouldStageItAloneAndLeaveTheOtherInTheWorkingTree()
    {
        using var fixture = await ChangedAsync();

        var result = await TakeAsync(fixture, "-e", PatchAction.Stage);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(IndexOutcomeText.Taken(PatchAction.Stage, false), result.Message);
        Assert.Equal("a\nb\nc\nd\nE\nf\ng\nh\ni\n", await fixture.GitAsync("show", ":sample.txt") + "\n");
        Assert.Equal(After, await File.ReadAllTextAsync(Path.Combine(fixture.Root, "sample.txt")));
    }

    [Fact]
    public async Task WhenOneOfTwoStagedHunksIsUnstaged_ShouldTakeItAloneOutOfTheIndex()
    {
        using var fixture = await ChangedAsync();
        await fixture.GitAsync("add", "sample.txt");

        var result = await TakeAsync(fixture, "-e", PatchAction.Unstage);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(IndexOutcomeText.Taken(PatchAction.Unstage, false), result.Message);
        Assert.Equal("a\nb\nc\nd\ne\nf\ng\nh\nI\n", await fixture.GitAsync("show", ":sample.txt") + "\n");
        Assert.Equal(After, await File.ReadAllTextAsync(Path.Combine(fixture.Root, "sample.txt")));
    }

    // CRLF lines and a last line without a newline reach the index byte for byte.
    [Fact]
    public async Task WhenTheFileEndsLinesWithCrlfAndHasNoFinalNewline_ShouldStageTheHunkByteForByte()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.WriteAsync("sample.txt", "a\r\nb\r\nc\r\nd\r\ne\r\nf\r\ng\r\nh\r\ni");
        await fixture.CommitAsync("Before", "sample.txt");
        await fixture.WriteAsync("sample.txt", "A\r\nb\r\nc\r\nd\r\ne\r\nf\r\ng\r\nh\r\nI");

        var result = await TakeAsync(fixture, "-i", PatchAction.Stage);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        var index = await fixture.RunAsync(["show", ":sample.txt"]);
        Assert.Equal("a\r\nb\r\nc\r\nd\r\ne\r\nf\r\ng\r\nh\r\nI", Encoding.UTF8.GetString(index.StandardOutput));
    }

    // The diff was shown, then the index changed under it: the snapshot is current, the patch is not, and Git refuses,
    // naming a hunk or chosen lines as the request did.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenThePatchNoLongerFitsTheIndex_ShouldRefuseAndLeaveTheIndexAsItWas(bool lines)
    {
        using var fixture = await ChangedAsync();
        var shown = await DiffAsync(fixture, fromIndex: false);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "sample.txt"), "a\nb\nc\nD\ne\nf\ng\nh\ni\n");
        await fixture.GitAsync("add", "sample.txt");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "sample.txt"), After);

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(
            new PatchRequest(await fixture.ReadAsync(), Sample(), Patch(shown, "-e", PatchAction.Stage), PatchAction.Stage, lines), fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, IndexOutcomeText.NotTaken(PatchAction.Stage, lines)), (result.Outcome, result.Message));
        Assert.Equal("a\nb\nc\nD\ne\nf\ng\nh\ni\n", await fixture.GitAsync("show", ":sample.txt") + "\n");
    }

    [Fact]
    public async Task WhenOneOfTwoHunksIsDiscarded_ShouldTakeItAloneOutOfTheWorkingTree()
    {
        using var fixture = await ChangedAsync();

        var result = await TakeAsync(fixture, "-e", PatchAction.Discard);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(IndexOutcomeText.Taken(PatchAction.Discard, false), result.Message);
        Assert.Equal("a\nb\nc\nd\ne\nf\ng\nh\nI\n", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "sample.txt")));
        Assert.Equal(Before, await fixture.GitAsync("show", ":sample.txt") + "\n");
    }

    // The working tree goes back to what the index holds, so a staged change to the file stays.
    [Fact]
    public async Task WhenAHunkOfAPartlyStagedFileIsDiscarded_ShouldKeepWhatIsStaged()
    {
        using var fixture = await ChangedAsync();
        await fixture.WriteAsync("sample.txt", "a\nb\nc\nd\nE\nf\ng\nh\ni\n");
        await fixture.GitAsync("add", "sample.txt");
        await fixture.WriteAsync("sample.txt", After);

        var result = await TakeAsync(fixture, "-i", PatchAction.Discard);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("a\nb\nc\nd\nE\nf\ng\nh\ni\n", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "sample.txt")));
        Assert.Equal("a\nb\nc\nd\nE\nf\ng\nh\ni\n", await fixture.GitAsync("show", ":sample.txt") + "\n");
    }

    [Fact]
    public async Task WhenTheFileEndsLinesWithCrlfAndHasNoFinalNewline_ShouldDiscardTheHunkByteForByte()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.WriteAsync("sample.txt", "a\r\nb\r\nc\r\nd\r\ne\r\nf\r\ng\r\nh\r\ni");
        await fixture.CommitAsync("Before", "sample.txt");
        await fixture.WriteAsync("sample.txt", "A\r\nb\r\nc\r\nd\r\ne\r\nf\r\ng\r\nh\r\nI");

        var result = await TakeAsync(fixture, "-i", PatchAction.Discard);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("A\r\nb\r\nc\r\nd\r\ne\r\nf\r\ng\r\nh\r\ni", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "sample.txt"))));
    }

    // With autocrlf the shown diff has no CR, while the working file ends its lines with CRLF; Git converts on the way
    // in and out, so the file keeps its line endings.
    [Fact]
    public async Task WhenGitConvertsLineEndingsInTheWorkingTree_ShouldDiscardTheHunkAndKeepCrlf()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "true");
        await fixture.WriteAsync("sample.txt", Before.Replace("\n", "\r\n"));
        await fixture.CommitAsync("Before", "sample.txt");
        await fixture.WriteAsync("sample.txt", After.Replace("\n", "\r\n"));

        var result = await TakeAsync(fixture, "-e", PatchAction.Discard);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("a\nb\nc\nd\ne\nf\ng\nh\nI\n".Replace("\n", "\r\n"), await File.ReadAllTextAsync(Path.Combine(fixture.Root, "sample.txt")));
    }

    // The diff was shown, then the hunk's lines changed again on disk: Git refuses, and the file stays as it is now.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenTheDiscardNoLongerFitsTheWorkingFile_ShouldRefuseAndLeaveItAsItIs(bool lines)
    {
        using var fixture = await ChangedAsync();
        var shown = await DiffAsync(fixture, fromIndex: false);
        const string Edited = "a\nb\nc\nd\nX\nf\ng\nh\nI\n";
        await fixture.WriteAsync("sample.txt", Edited);

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(
            new PatchRequest(await fixture.ReadAsync(), Sample(), Patch(shown, "-e", PatchAction.Discard), PatchAction.Discard, lines), fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, IndexOutcomeText.NotTaken(PatchAction.Discard, lines)), (result.Outcome, result.Message));
        Assert.Equal(Edited, await File.ReadAllTextAsync(Path.Combine(fixture.Root, "sample.txt")));
    }

    // Chosen lines go alone: the added E is staged beside the e the index keeps.
    [Fact]
    public async Task WhenOnlyTheAddedLineOfAChangeIsStaged_ShouldStageItBesideTheRemovedOne()
    {
        using var fixture = await ChangedAsync();

        var result = await TakeLinesAsync(fixture, ["+E"], PatchAction.Stage);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(IndexOutcomeText.Taken(PatchAction.Stage, true), result.Message);
        Assert.Equal("a\nb\nc\nd\ne\nE\nf\ng\nh\ni\n", await fixture.GitAsync("show", ":sample.txt") + "\n");
    }

    [Fact]
    public async Task WhenOnlyTheAddedLineOfAStagedChangeIsUnstaged_ShouldKeepTheRemovalStaged()
    {
        using var fixture = await ChangedAsync();
        await fixture.GitAsync("add", "sample.txt");

        var result = await TakeLinesAsync(fixture, ["+E"], PatchAction.Unstage);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("a\nb\nc\nd\nf\ng\nh\nI\n", await fixture.GitAsync("show", ":sample.txt") + "\n");
    }

    [Fact]
    public async Task WhenOnlyTheRemovedLineOfAChangeIsDiscarded_ShouldBringItBackBesideTheAddedOne()
    {
        using var fixture = await ChangedAsync();

        var result = await TakeLinesAsync(fixture, ["-e"], PatchAction.Discard);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(IndexOutcomeText.Taken(PatchAction.Discard, true), result.Message);
        Assert.Equal("a\nb\nc\nd\ne\nE\nf\ng\nh\nI\n", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "sample.txt")));
    }

    [Fact]
    public async Task WhenLinesOfBothChangesAreStaged_ShouldStageThemInOnePatch()
    {
        using var fixture = await ChangedAsync();

        var result = await TakeLinesAsync(fixture, ["+E", "+I"], PatchAction.Stage);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("a\nb\nc\nd\ne\nE\nf\ng\nh\ni\nI\n", await fixture.GitAsync("show", ":sample.txt") + "\n");
    }

    // A line in another encoding, such as Windows-1252, is shown with replacement characters, and a patch made from the
    // diff would write them where the line comes back: into the index on a stage or an unstage, into the file on a
    // discard. Only the whole file can be taken, and the index and the file stay byte for byte.
    [Theory]
    [InlineData(PatchAction.Stage, "e", "été", "+�t�")]
    [InlineData(PatchAction.Unstage, "été", "E", "+E")]
    [InlineData(PatchAction.Discard, "été", "E", "+E")]
    public async Task WhenALineOfTheChangeIsNotUtf8_ShouldRefuseToTakePartOfTheFileAndChangeNothing(PatchAction action, string committed, string changed, string line)
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        var file = Path.Combine(fixture.Root, "sample.txt");
        await File.WriteAllBytesAsync(file, Encoding.Latin1.GetBytes(Before.Replace("\ne\n", "\n" + committed + "\n")), fixture.Token);
        await fixture.CommitAsync("Before", "sample.txt");
        await File.WriteAllBytesAsync(file, Encoding.Latin1.GetBytes(Before.Replace("\ne\n", "\n" + changed + "\n")), fixture.Token);
        if (action == PatchAction.Unstage) await fixture.GitAsync("add", "sample.txt");
        var index = (await fixture.RunAsync(["show", ":sample.txt"])).StandardOutput;
        var working = await File.ReadAllBytesAsync(file, fixture.Token);

        var result = await TakeAsync(fixture, line, action);

        Assert.Equal((GitWriteOutcome.Rejected, WriteText.PartNotUtf8), (result.Outcome, result.Message));
        Assert.Equal(index, (await fixture.RunAsync(["show", ":sample.txt"])).StandardOutput);
        Assert.Equal(working, await File.ReadAllBytesAsync(file, fixture.Token));
    }

    // With diff.suppressBlankEmpty Git prints unchanged blank lines empty; the hunk Trag builds around them must still be
    // one Git applies, and only that hunk reaches the index.
    [Fact]
    public async Task WhenGitSuppressesTheSpaceOfBlankContextLines_ShouldStillStageTheHunkAlone()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.GitAsync("config", "diff.suppressBlankEmpty", "true");
        await fixture.WriteAsync("sample.txt", "a\n\nb\n\nc\nd\ne\nf\ng\n\nh\ni\n");
        await fixture.CommitAsync("Before", "sample.txt");
        await fixture.WriteAsync("sample.txt", "a\n\nB\n\nc\nd\ne\nf\ng\n\nh\nI\n");

        var result = await TakeAsync(fixture, "+B", PatchAction.Stage);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("a\n\nB\n\nc\nd\ne\nf\ng\n\nh\ni\n", await fixture.GitAsync("show", ":sample.txt") + "\n");
    }

    // The last line without a newline, changed: its hunk is staged, unstaged and discarded like any other.
    [Fact]
    public async Task WhenTheHunkIsAtTheEndOfTheFileWithoutAFinalNewline_ShouldStageUnstageAndDiscardIt()
    {
        using var fixture = await CommittedAsync("sample.txt", "a\nb\nc", "a\nb\nC");

        var staged = await TakeAtAsync(fixture, "sample.txt", "-c", PatchAction.Stage);
        var stagedText = await fixture.GitAsync("show", ":sample.txt");
        var unstaged = await TakeAtAsync(fixture, "sample.txt", "+C", PatchAction.Unstage);
        var unstagedText = await fixture.GitAsync("show", ":sample.txt");
        var discarded = await TakeAtAsync(fixture, "sample.txt", "-c", PatchAction.Discard);

        Assert.Equal((GitWriteOutcome.Succeeded, GitWriteOutcome.Succeeded, GitWriteOutcome.Succeeded), (staged.Outcome, unstaged.Outcome, discarded.Outcome));
        Assert.Equal(("a\nb\nC", "a\nb\nc"), (stagedText, unstagedText));
        Assert.Equal("a\nb\nc", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "sample.txt"), fixture.Token));
    }

    // One unchanged line between two changes: the other change is context as the index has it, and stays unstaged.
    [Fact]
    public async Task WhenTwoBlocksShareContext_ShouldTakeOneWithoutTheOther()
    {
        using var fixture = await CommittedAsync("sample.txt", Before, "a\nb\nc\nd\nE\nf\nG\nh\ni\n");

        var result = await TakeAtAsync(fixture, "sample.txt", "-e", PatchAction.Stage);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("a\nb\nc\nd\nE\nf\ng\nh\ni", await fixture.GitAsync("show", ":sample.txt"));
        Assert.Equal("a\nb\nc\nd\nE\nf\nG\nh\ni\n", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "sample.txt"), fixture.Token));
    }

    // The name goes into the patch's --- and +++ lines as it is.
    [Fact]
    public async Task WhenTheFileNameHasSpacesBracketsAndNonAscii_ShouldStageAndDiscardAHunk()
    {
        const string name = "docs/a [x] č.txt";
        using var fixture = await CommittedAsync(name, Before, After);

        var staged = await TakeAtAsync(fixture, name, "-e", PatchAction.Stage);
        var discarded = await TakeAtAsync(fixture, name, "-i", PatchAction.Discard);

        Assert.Equal((GitWriteOutcome.Succeeded, GitWriteOutcome.Succeeded), (staged.Outcome, discarded.Outcome));
        Assert.Equal("a\nb\nc\nd\nE\nf\ng\nh\ni", await fixture.GitAsync("show", ":" + name, "--"));
        Assert.Equal("a\nb\nc\nd\nE\nf\ng\nh\ni\n", await File.ReadAllTextAsync(Path.Combine(fixture.Root, name), fixture.Token));
    }

    // x.txt has the same content and the same change beside the file, so a patch that reached it would apply there. A
    // name Git would escape in a patch is offered no hunk, so off Windows such a row has nothing to take.
    [Theory]
    [MemberData(nameof(HostileNameRows.Tracked), MemberType = typeof(HostileNameRows))]
    public async Task WhenTheFileHasAHostileName_ShouldStageAndDiscardOneHunkOfIt(string label)
    {
        var name = TestNames.Find(label).Path;
        using var fixture = await CommittedAsync(name, Before, After);
        if (!OperatingSystem.IsWindows() && !HunkPatch.Fits(Assert.Single((await fixture.ReadAsync()).Files), staged: false)) return;
        await fixture.WriteAsync("x.txt", Before);
        await fixture.CommitAsync("Decoy", "x.txt");
        await fixture.WriteAsync("x.txt", After);

        var staged = await TakeAtAsync(fixture, name, "-e", PatchAction.Stage);
        var discarded = await TakeAtAsync(fixture, name, "-i", PatchAction.Discard);

        Assert.Equal((GitWriteOutcome.Succeeded, GitWriteOutcome.Succeeded), (staged.Outcome, discarded.Outcome));
        Assert.Equal("a\nb\nc\nd\nE\nf\ng\nh\ni", await fixture.GitAsync("show", ":" + name, "--"));
        Assert.Equal("a\nb\nc\nd\nE\nf\ng\nh\ni\n", await File.ReadAllTextAsync(Path.Combine(fixture.Root, name), fixture.Token));
        Assert.Equal((Before.TrimEnd(), After), (await fixture.GitAsync("show", ":x.txt"), await File.ReadAllTextAsync(Path.Combine(fixture.Root, "x.txt"), fixture.Token)));
    }

    // A stage applies to the index alone: an edit of the working file elsewhere after the diff was shown does not keep
    // the shown hunk from going in.
    [Fact]
    public async Task WhenTheWorkingFileChangedElsewhereAfterTheDiffWasShown_ShouldStillStageTheShownHunkIntoTheIndex()
    {
        using var fixture = await CommittedAsync("sample.txt", Before, After);
        var shown = await fixture.ReadAsync();
        var diff = await DiffAtAsync(fixture, "sample.txt", fromIndex: false);
        await fixture.WriteAsync("sample.txt", "top\n" + After);

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(
            new PatchRequest(shown, Named("sample.txt"), PatchAt(diff, "-e", "sample.txt", PatchAction.Stage), PatchAction.Stage, Lines: false), fixture.Token);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("a\nb\nc\nd\nE\nf\ng\nh\ni", await fixture.GitAsync("show", ":sample.txt"));
    }

    // The index holds the file under its new name, and so does the patch.
    [Fact]
    public async Task WhenTheIndexHoldsAStagedRenameAndTheFileIsModified_ShouldStageTheHunkUnderTheNewName()
    {
        using var fixture = await CommittedAsync("sample.txt", Before, Before);
        await fixture.GitAsync("mv", "sample.txt", "renamed.txt");
        await fixture.WriteAsync("renamed.txt", After);

        var result = await TakeAtAsync(fixture, "renamed.txt", "-e", PatchAction.Stage);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal("a\nb\nc\nd\nE\nf\ng\nh\ni", await fixture.GitAsync("show", ":renamed.txt"));
    }

    // git apply follows apply.ignoreWhitespace; a context line whose spaces changed since the diff was shown is still a
    // file other than the one shown.
    [Fact]
    public async Task WhenApplyIgnoreWhitespaceIsSetInConfig_ShouldStillRefuseADriftedFile()
    {
        using var fixture = await CommittedAsync("sample.txt", Before, After);
        await fixture.GitAsync("config", "apply.ignoreWhitespace", "change");
        var shown = await fixture.ReadAsync();
        var diff = await DiffAtAsync(fixture, "sample.txt", fromIndex: false);
        var drifted = After.Replace("d\n", "d  \n", StringComparison.Ordinal);
        await fixture.WriteAsync("sample.txt", drifted);

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(
            new PatchRequest(shown, Named("sample.txt"), PatchAt(diff, "-e", "sample.txt", PatchAction.Discard), PatchAction.Discard, Lines: false), fixture.Token);

        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.Equal(drifted, await File.ReadAllTextAsync(Path.Combine(fixture.Root, "sample.txt"), fixture.Token));
    }

    // Staging only the added line of a last line changed without a newline: the line it replaces keeps its place and
    // now ends with a newline, so the two stay two lines.
    [Fact]
    public async Task WhenOnlyTheAddedLineOfALastLineWithoutANewlineIsStaged_ShouldKeepBothLines()
    {
        using var fixture = await CommittedAsync("sample.txt", "a\nb", "a\nB");

        var result = await TakeLinesAtAsync(fixture, "sample.txt", ["+B"], PatchAction.Stage);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("a\nb\nB", await fixture.GitAsync("show", ":sample.txt"));
    }

    // Discarding only the removal of such a line brings it back before the line that replaced it, as its own line.
    [Fact]
    public async Task WhenOnlyTheRemovedLineOfALastLineWithoutANewlineIsDiscarded_ShouldKeepBothLines()
    {
        using var fixture = await CommittedAsync("sample.txt", "a\nb", "a\nB");

        var result = await TakeLinesAtAsync(fixture, "sample.txt", ["-b"], PatchAction.Discard);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("a\nb\nB", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "sample.txt"), fixture.Token));
    }

    // Git turns the working file's CRLF into the index's LF before it diffs, by core.autocrlf or by an eol=crlf attribute:
    // the hunk reaches the index with LF, and the working file keeps its CRLF.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAutocrlfIsTrueOrEolIsCrlf_ShouldStageAHunkAndKeepTheWorkingFile(bool attribute)
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", attribute ? "false" : "true");
        if (attribute) await fixture.WriteAsync(".gitattributes", "sample.txt text eol=crlf\n");
        await fixture.WriteAsync("sample.txt", Before.Replace("\n", "\r\n"));
        await fixture.CommitAsync("Before");
        await fixture.WriteAsync("sample.txt", After.Replace("\n", "\r\n"));

        var result = await TakeAtAsync(fixture, "sample.txt", "-e", PatchAction.Stage);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("a\nb\nc\nd\nE\nf\ng\nh\ni\n", Encoding.UTF8.GetString((await fixture.RunAsync(["show", ":sample.txt"])).StandardOutput));
        Assert.Equal(After.Replace("\n", "\r\n"), await File.ReadAllTextAsync(Path.Combine(fixture.Root, "sample.txt"), fixture.Token));
    }

    // Git converts a file by its working-tree-encoding or its filter both ways: the diff shows the text the index holds, a
    // stage applies to the index, and a discard reads the working file through the conversion and writes it back through
    // it. rot13 turns lower-case letters only, so the upper-case lines read the same on both sides.
    [Theory]
    [InlineData("sample.txt working-tree-encoding=UTF-16LE-BOM text eol=lf\n")]
    [InlineData("sample.txt filter=rot13\n")]
    public async Task WhenWorkingTreeEncodingIsUtf16OrACustomFilterApplies_ShouldStageAndDiscardTheHunkThroughGitsConversion(string attributes)
    {
        var utf16 = attributes.Contains("UTF-16", StringComparison.Ordinal);
        byte[] Working(string text) => utf16 ? [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)] : Encoding.UTF8.GetBytes(text);
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.GitAsync("config", "filter.rot13.clean", "tr a-z n-za-m");
        await fixture.GitAsync("config", "filter.rot13.smudge", "tr a-z n-za-m");
        await fixture.WriteAsync(".gitattributes", attributes);
        var file = Path.Combine(fixture.Root, "sample.txt");
        await File.WriteAllBytesAsync(file, Working(Before), fixture.Token);
        await fixture.CommitAsync("Before");
        await File.WriteAllBytesAsync(file, Working(After), fixture.Token);

        var shown = await DiffAtAsync(fixture, "sample.txt", fromIndex: false);
        var staged = await TakeAtAsync(fixture, "sample.txt", "+E", PatchAction.Stage);
        var discarded = await TakeAtAsync(fixture, "sample.txt", "+I", PatchAction.Discard);

        Assert.Equal(DiffContentKind.Text, shown.Kind);
        Assert.Equal((GitWriteOutcome.Succeeded, GitWriteOutcome.Succeeded), (staged.Outcome, discarded.Outcome));
        Assert.Equal(Working("a\nb\nc\nd\nE\nf\ng\nh\ni\n"), await File.ReadAllBytesAsync(file, fixture.Token));
        Assert.Equal("M  sample.txt", await fixture.GitAsync("status", "--porcelain"));
    }

    // With core.symlinks off a link is a file holding its target, and Git diffs that text: a hunk of it stages the new
    // target as staging the whole file would, and the index keeps the link's mode.
    [Fact]
    public async Task WhenTheRowIsASymlink_ShouldStageItsNewTargetAndKeepItALink()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.symlinks", "false");
        await fixture.WriteAsync("link", "target-a.txt");
        var target = await fixture.GitAsync("hash-object", "-w", "link");
        await fixture.GitAsync("update-index", "--add", "--cacheinfo", "120000," + target + ",link");
        await fixture.GitAsync("commit", "-m", "Link");
        await fixture.WriteAsync("link", "target-b.txt");

        var result = await TakeAtAsync(fixture, "link", "+target-b.txt", PatchAction.Stage);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.StartsWith("120000 ", await fixture.GitAsync("ls-files", "--stage", "--", "link"));
        Assert.Equal("target-b.txt", await fixture.GitAsync("show", ":link"));
    }

    // A staged change of the executable bit beside staged lines: a hunk is unstaged alone, and the mode stays staged until
    // the whole file is unstaged, as git reset -p leaves it until it is asked about.
    [Fact]
    public async Task WhenTheExecutableBitIsStagedBesideTheLines_ShouldUnstageTheHunkAloneAndKeepTheMode()
    {
        using var fixture = await ChangedAsync();
        await fixture.GitAsync("add", "sample.txt");
        await fixture.GitAsync("update-index", "--chmod=+x", "sample.txt");

        var result = await TakeAsync(fixture, "+E", PatchAction.Unstage);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.StartsWith("100755 ", await fixture.GitAsync("ls-files", "--stage", "--", "sample.txt"));
        Assert.Equal("a\nb\nc\nd\ne\nf\ng\nh\nI", await fixture.GitAsync("show", ":sample.txt"));
    }

    // A diff attribute makes Git diff a file with a NUL byte as text, and a BOM belongs to the first line: both reach the
    // patch as they are, as context of the hunk below them, and the hunk is staged byte for byte.
    [Theory]
    [InlineData("data.bin", "a\0")]
    [InlineData("bom.txt", "﻿a")]
    public async Task WhenTheFileHasNulBytesForcedToTextOrABomOnLineOne_ShouldStageTheHunk(string name, string first)
    {
        var committed = first + "\nb\nc\nd\ne\nf\ng\nh\ni\n";
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.WriteAsync(".gitattributes", "*.bin diff\n");
        await fixture.WriteAsync(name, committed);
        await fixture.CommitAsync("Committed");
        await fixture.WriteAsync(name, committed.Replace("\nb\n", "\nB\n").Replace("\ni\n", "\nI\n"));

        var result = await TakeAtAsync(fixture, name, "-b", PatchAction.Stage);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(Encoding.UTF8.GetBytes(committed.Replace("\nb\n", "\nB\n")), (await fixture.RunAsync(["show", ":" + name, "--"])).StandardOutput);
    }

    // The content committed under the name, then changed in the working tree.
    private static async Task<GitFixture> CommittedAsync(string path, string committed, string changed)
    {
        var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.WriteAsync(path, committed);
        await fixture.CommitAsync("Committed", path);
        await fixture.WriteAsync(path, changed);
        return fixture;
    }

    private static async Task<GitWriteResult> TakeAtAsync(GitFixture fixture, string path, string line, PatchAction action)
    {
        var diff = await DiffAtAsync(fixture, path, fromIndex: action == PatchAction.Unstage);
        return await new GitWriteRouter(fixture.Runner).ExecuteAsync(
            new PatchRequest(await fixture.ReadAsync(), Named(path), PatchAt(diff, line, path, action), action, Lines: false), fixture.Token);
    }

    private static async Task<GitWriteResult> TakeLinesAtAsync(GitFixture fixture, string path, string[] texts, PatchAction action)
    {
        var diff = await DiffAtAsync(fixture, path, fromIndex: action == PatchAction.Unstage);
        var chosen = texts.Select(text => diff.Lines.ToList().FindIndex(line => line.Text == text)).ToArray();
        return await new GitWriteRouter(fixture.Runner).ExecuteAsync(
            new PatchRequest(await fixture.ReadAsync(), Named(path), HunkPatch.For(diff.Lines, chosen, Named(path), action), action, Lines: true), fixture.Token);
    }

    private static async Task<FileDiff> DiffAtAsync(GitFixture fixture, string path, bool fromIndex)
    {
        var file = (await fixture.ReadAsync()).Files.Single(file => file.Path.Display == path && (fromIndex ? file.IsStaged : file.IsUnstaged));
        return await new CliFileDiffReader(new()).ReadAsync(fixture.Root, file, fromIndex, false, fixture.Token);
    }

    private static string PatchAt(FileDiff diff, string line, string path, PatchAction action)
    {
        var index = diff.Lines.ToList().FindIndex(candidate => candidate.Text == line);
        return HunkPatch.For(diff.Lines, DiffBlocks.At(diff.Lines, index)!.Value.Lines, Named(path), action);
    }

    private static Trag.Domain.Paths.GitPath Named(string path) => new(Encoding.UTF8.GetBytes(path));

    // Nine lines committed; e and i changed in the working tree.
    private static async Task<GitFixture> ChangedAsync()
    {
        var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.WriteAsync("sample.txt", Before);
        await fixture.CommitAsync("Before", "sample.txt");
        await fixture.WriteAsync("sample.txt", After);
        return fixture;
    }

    // The diff of the side the action reads, the hunk that starts with the line, and its patch sent as Trag sends it.
    private static async Task<GitWriteResult> TakeAsync(GitFixture fixture, string line, PatchAction action)
    {
        var diff = await DiffAsync(fixture, fromIndex: action == PatchAction.Unstage);
        return await new GitWriteRouter(fixture.Runner).ExecuteAsync(new PatchRequest(await fixture.ReadAsync(), Sample(), Patch(diff, line, action), action, Lines: false), fixture.Token);
    }

    // The shown lines with those texts, sent as Trag sends a choice of lines.
    private static async Task<GitWriteResult> TakeLinesAsync(GitFixture fixture, string[] texts, PatchAction action)
    {
        var diff = await DiffAsync(fixture, fromIndex: action == PatchAction.Unstage);
        var chosen = texts.Select(text => diff.Lines.ToList().FindIndex(line => line.Text == text)).ToArray();
        var patch = HunkPatch.For(diff.Lines, chosen, Sample(), action);
        return await new GitWriteRouter(fixture.Runner).ExecuteAsync(new PatchRequest(await fixture.ReadAsync(), Sample(), patch, action, Lines: true), fixture.Token);
    }

    private static async Task<FileDiff> DiffAsync(GitFixture fixture, bool fromIndex)
    {
        var file = (await fixture.ReadAsync()).Files.Single(file => fromIndex ? file.IsStaged : file.IsUnstaged);
        return await new CliFileDiffReader(new()).ReadAsync(fixture.Root, file, fromIndex, false, fixture.Token);
    }

    private static string Patch(FileDiff diff, string line, PatchAction action)
    {
        var index = diff.Lines.ToList().FindIndex(candidate => candidate.Text == line);
        return HunkPatch.For(diff.Lines, DiffBlocks.At(diff.Lines, index)!.Value.Lines, Sample(), action);
    }

    private static Trag.Domain.Paths.GitPath Sample() => new("sample.txt"u8.ToArray());
}
