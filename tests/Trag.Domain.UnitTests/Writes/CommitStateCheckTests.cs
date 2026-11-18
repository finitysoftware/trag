using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Writes;

// How a commit is checked against the repository it was made in, and what the outcome says. A refused commit: Git that
// could not write the commit while it signs commits could not sign it, and that is said with what to check; any other
// refusal is Git's, with its output below.
public sealed class CommitStateCheckTests
{
    private const string NotWritten = "error: cannot spawn missing-keygen: No such file or directory\nerror: \nfatal: failed to write commit object";

    [Fact]
    public void WhenGitCouldNotWriteACommitItSigns_ShouldSayTheCommitCouldNotBeSigned() =>
        Assert.Equal(CommitOutcomeText.NotSigned(CommitSigning.Ssh), Check(CommitSigning.Ssh).Refusal(NotWritten));

    [Fact]
    public void WhenCommitsAreNotSigned_ShouldLeaveTheRefusalToGit() =>
        Assert.Equal(CommitOutcomeText.Rejected, Check(CommitSigning.None).Refusal(NotWritten));

    [Fact]
    public void WhenAHookRefusesASignedCommit_ShouldLeaveTheRefusalToGit() =>
        Assert.Equal(CommitOutcomeText.Rejected, Check(CommitSigning.Ssh).Refusal("pre-commit: tests failed"));

    [Theory]
    [InlineData(CommitSigning.Ssh, "Git could not sign the commit with your SSH key. A key with a passphrase has to be in an ssh-agent the signing program reaches: for Windows' own agent, set gpg.ssh.program to C:/Windows/System32/OpenSSH/ssh-keygen.exe. Your message and staged changes are kept.")]
    [InlineData(CommitSigning.OpenPgp, "Git could not sign the commit with your GPG key. Check that gpg and its agent can sign outside Trag. Your message and staged changes are kept.")]
    [InlineData(CommitSigning.X509, "Git could not sign the commit with your X.509 certificate. Check that gpgsm can sign outside Trag. Your message and staged changes are kept.")]
    public void WhenACommitCouldNotBeSigned_ShouldSayWhatToCheckForItsKindOfKey(CommitSigning signing, string expected) =>
        Assert.Equal(expected, CommitOutcomeText.NotSigned(signing));

    // The parents a new commit is checked against: HEAD's commit, and for a merge the incoming one too; an amend keeps
    // those of the commit it replaces, and a first commit has none.
    [Fact]
    public void WhenACommitIsChecked_ShouldExpectTheParentsItWasMadeOn()
    {
        var merging = Snapshots.Create(operation: RepositoryOperation.Merge, merge: new(Snapshots.Id('b'), Snapshots.HeadId, "m", "fp", ""));

        Assert.Equal(Snapshots.HeadId, new CommitStateCheck(Snapshots.Create(), null, "main").Parents);
        Assert.Equal(Snapshots.HeadId + " " + Snapshots.Id('b'), new CommitStateCheck(merging, null, "main").Parents);
        Assert.Equal(Snapshots.Id('c') + " " + Snapshots.Id('d'), new CommitStateCheck(merging, null, "main", Snapshots.Id('c') + " " + Snapshots.Id('d')).Parents);
        Assert.Equal("", new CommitStateCheck(Snapshots.Create(unborn: true), null, "main").Parents);
    }

    [Fact]
    public void WhenHeadOrTheBranchIsNotWhereItWasBefore_ShouldSayTheRepositoryMoved()
    {
        Assert.False(new CommitStateCheck(Snapshots.Create(), Snapshots.HeadId, "main").HasMoved);
        Assert.True(new CommitStateCheck(Snapshots.Create(), Snapshots.Id('e'), "main").HasMoved);
        Assert.True(new CommitStateCheck(Snapshots.Create(), Snapshots.HeadId, "other").HasMoved);
    }

    // Without the reflog entry, only a Git that exited cleanly with HEAD moved on the same branch may have made the commit.
    [Fact]
    public void WhenGitExitedCleanlyAndHeadMovedOnTheSameBranch_ShouldLetTheNewHeadBeChecked()
    {
        var moved = new CommitStateCheck(Snapshots.Create(), Snapshots.Id('e'), "main");

        Assert.True(moved.CanUseHead(0));
        Assert.False(moved.CanUseHead(1));
        Assert.False(moved.CanUseHead(null));
        Assert.False(new CommitStateCheck(Snapshots.Create(), Snapshots.HeadId, "main").CanUseHead(0));
        Assert.False(new CommitStateCheck(Snapshots.Create(), Snapshots.Id('e'), "other").CanUseHead(0));
    }

    [Fact]
    public void WhenTheNewCommitHoldsTheIndexTreeOnTheExpectedParents_ShouldMatchTheIndex()
    {
        var check = new CommitStateCheck(Snapshots.Create(), Snapshots.Id('e'), "main");

        Assert.True(check.MatchesIndex((Snapshots.HeadId, "tree"), "tree"));
        Assert.False(check.MatchesIndex((Snapshots.HeadId, "tree"), null));
        Assert.False(check.MatchesIndex((Snapshots.Id('c'), "tree"), "tree"));
        Assert.False(check.MatchesIndex((Snapshots.HeadId, "tree"), "other tree"));
    }

    [Fact]
    public void WhenHeadIsTheCommittedIdOnTheSameBranch_ShouldMatchTheCommit()
    {
        Assert.True(new CommitStateCheck(Snapshots.Create(), Snapshots.Id('e'), "main").MatchesCommit(Snapshots.Id('e')));
        Assert.False(new CommitStateCheck(Snapshots.Create(), Snapshots.Id('e'), "main").MatchesCommit(Snapshots.Id('f')));
        Assert.False(new CommitStateCheck(Snapshots.Create(), Snapshots.Id('e'), "other").MatchesCommit(Snapshots.Id('e')));
    }

    [Fact]
    public void WhenACommitIsVerified_ShouldSayWhetherItWasCreatedAmendedMergedOrMadeAfterCancel()
    {
        var merging = Snapshots.Create(operation: RepositoryOperation.Merge, merge: new(Snapshots.Id('b'), Snapshots.HeadId, "m", "fp", ""));

        Assert.Equal(CommitOutcomeText.Created, new CommitStateCheck(Snapshots.Create(), null, "main").SuccessMessage(false));
        Assert.Equal(CommitOutcomeText.MergeCreated, new CommitStateCheck(merging, null, "main").SuccessMessage(false));
        Assert.Equal(CommitOutcomeText.Amended, new CommitStateCheck(Snapshots.Create(), null, "main", Snapshots.Id('c')).SuccessMessage(false));
        Assert.Equal(CommitOutcomeText.CreatedAfterCancel, new CommitStateCheck(Snapshots.Create(), null, "main", Snapshots.Id('c')).SuccessMessage(true));
    }

    private static CommitStateCheck Check(CommitSigning signing) => new(Snapshots.Create() with { Signing = signing }, Snapshots.HeadId, "main");
}
