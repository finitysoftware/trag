using Trag.Domain.History;
using Trag.TestSupport;
using Trag.ViewModels.History;

namespace Trag.ViewModels.UnitTests.History;

public sealed class HistoryModelsTests
{
    [Theory]
    [InlineData(16_383)]
    [InlineData(16_384)]
    [InlineData(16_385)]
    public void WhenACommitMessageIsAroundTheDisplayLimit_ShouldCutOnlyTheShownBodyAndKeepTheCompleteMessage(int length)
    {
        var message = new string('x', length);
        var details = new CommitDetails(Snapshots.Commit("head", "subject"), message, []);
        var panel = CommitPanel.From(details);
        Assert.Same(message, panel.Details!.Message);
        if (length <= 16_384) Assert.Same(message, panel.Body);
        else
        {
            Assert.StartsWith(message[..16_384] + "\n…", panel.Body);
            Assert.Contains("search uses the complete message", panel.Body);
        }
    }

    [Theory]
    [InlineData(0, "", "This commit has no file changes against this parent.")]
    [InlineData(1, "", "1 changed file")]
    [InlineData(1, "Limited file list", "Limited file list")]
    public void WhenACommitsFilesAreShown_ShouldTakeTheirNoticeFromTheCommitResultAndSelectNoFile(int count, string notice, string expected)
    {
        var files = Enumerable.Range(0, count).Select(_ => new Trag.Domain.Diff.FileDiffMetadata(Snapshots.Path("a"), "M", "", "", "", "")).ToArray();
        var panel = CommitPanel.From(new(Snapshots.Commit("head", "subject") with { Parents = ["parent"] }, "body", files, notice));
        Assert.Equal(expected, panel.FileNotice);
        Assert.Equal(count, panel.Files.Count);
        Assert.Null(panel.SelectedFile);
    }

    [Fact]
    public void WhenASignedCommitIsShown_ShouldSayWhoSignedIt()
    {
        var details = new CommitDetails(Snapshots.Commit("head", "subject"), "body", []) { Signature = new(SignatureCheck.Good, "ana@example.invalid") };
        Assert.Equal("Signed by ana@example.invalid.", CommitPanel.From(details).Signature);
    }

    [Fact]
    public void WhenTheCommitCreditsACoAuthor_ShouldNameThemUnderTheAuthor()
    {
        var details = new CommitDetails(Snapshots.Commit("head", "subject"), "subject\n\nCo-authored-by: Ana Anić <ana@example.invalid>\n", []);
        Assert.Equal("with Ana Anić", CommitPanel.From(details).CoAuthorText);
    }
}
