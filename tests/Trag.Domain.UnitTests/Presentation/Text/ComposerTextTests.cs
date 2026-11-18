using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class ComposerTextTests
{
    [Fact]
    public void WhenNoRepositoryIsOpen_ShouldSayTheGitIdentityIsNotAvailable() => Assert.Equal("Git identity is not available.", CommitText.Author(null));

    [Fact]
    public void WhenGitHasNoIdentity_ShouldSayToSetUserNameAndEmail() =>
        Assert.StartsWith("Set Git user.name", CommitText.Author(Snapshots.Create(identity: false)));

    [Fact]
    public void WhenGitHasAnIdentity_ShouldNameTheAuthor() =>
        Assert.Equal("Author: Fixture <fixture@example.invalid>", CommitText.Author(Snapshots.Create()));

    [Theory]
    [InlineData(CommitSigning.OpenPgp, "Commits are signed with your GPG key.")]
    [InlineData(CommitSigning.Ssh, "Commits are signed with your SSH key.")]
    [InlineData(CommitSigning.X509, "Commits are signed with your X.509 certificate.")]
    [InlineData(CommitSigning.None, "")]
    public void WhenCommitsAreSigned_ShouldSayWithWhatKindOfKey(CommitSigning signing, string expected) => Assert.Equal(expected, CommitText.Signing(signing));

    [Fact]
    public void WhenTheCommitterIsTheAuthor_ShouldNotNameTheCommitter() => Assert.Equal("", CommitText.Committer(Snapshots.Create()));

    [Fact]
    public void WhenTheCommitterDiffersFromTheAuthor_ShouldNameTheCommitter() =>
        Assert.Equal("Committer: Bot <bot@example.invalid>", CommitText.Committer(Snapshots.Create() with { Committer = new("Bot", "bot@example.invalid") }));

    [Fact]
    public void WhenTheCommitterIsMissing_ShouldSaySo() =>
        Assert.Equal("Committer identity is missing.", CommitText.Committer(Snapshots.Create() with { Committer = null }));

    [Fact]
    public void WhenOneFileIsSelected_ShouldCaptionTheStageButtonStageSelected() => Assert.Equal("Stage selected", CommitText.StageAction(1));

    [Fact]
    public void WhenSeveralFilesAreSelected_ShouldCountThemOnTheUnstageButton() => Assert.Equal("Unstage 3 files", CommitText.UnstageAction(3));

    [Fact]
    public void WhenARowIsStaged_ShouldSayItIsReadFromTheIndex() => Assert.Equal("Staged · index", StatusText.Source(true));

    [Fact]
    public void WhenAFileIsModified_ShouldDescribeItAsModified() => Assert.Equal("Modified", StatusText.Description(Snapshots.Modified("a"), false));

    // A conflict stays unresolved until its resolution is staged.
    [Fact]
    public void WhenAFileIsInConflict_ShouldShowItAsAnUnresolvedConflict()
    {
        Assert.Equal("Unresolved conflict", StatusText.Description(Snapshots.Conflict("c"), false));
        Assert.Equal("!", StatusText.Code(Snapshots.Conflict("c"), true));
    }

    [Fact]
    public void WhenOneUntrackedFileIsDiscarded_ShouldAskToDeleteItAndSayThereIsNoUndo()
    {
        var one = new DiscardQuestion(DiscardSet.From([Snapshots.Untracked("u")]));
        Assert.Equal("Delete untracked file?", one.Title);
        Assert.Equal("1 untracked file is deleted from disk. There is no undo.", one.Description);
    }

    [Fact]
    public void WhenManyChangedFilesAreDiscarded_ShouldNameTheFirstFourAndSayTheirChangesAreLost()
    {
        var many = new DiscardQuestion(DiscardSet.From(Enumerable.Range(0, 6).Select(i => Snapshots.Modified("f" + i))));
        Assert.Equal("Discard changes in 6 files?", many.Title);
        Assert.Equal("f0, f1, f2, f3 and 2 more", many.Subtitle);
        Assert.StartsWith("The working tree changes to 6 files are lost", many.Description);
    }

    [Fact]
    public void WhenHeadMovedAwayFromWhereTheMergeStarted_ShouldSayTheOriginalHeadChanged()
    {
        var (merge, merging) = Merging();
        Assert.StartsWith("The original HEAD", MergeText.Unsupported(merging with { Merge = merge with { OriginalHead = Snapshots.Id('b') } }));
    }

    [Fact]
    public void WhenNoMergeIsInProgress_ShouldSayNothing() => Assert.Equal("", MergeText.Unsupported(Snapshots.Create()));

    // A merge of incoming into the current HEAD with no conflict left.
    private static (RepositoryMerge Merge, RepositorySnapshot Merging) Merging()
    {
        var merge = new RepositoryMerge("incoming", Snapshots.HeadId, "Merge", "fingerprint", "");
        return (merge, Snapshots.Create(operation: RepositoryOperation.Merge, merge: merge));
    }
}
