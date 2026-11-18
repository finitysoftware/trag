using Trag.Domain.Sync;

namespace Trag.Domain.UnitTests.Sync;

// A push, a publish and a remote branch deletion are judged by the same four verdicts over what the remote says now.
// What differs between them is only what they expected to find afterwards, so that is the only thing that varies here.
public sealed class RemoteTipCheckTests
{
    private static readonly string Old = new('a', 40);
    private static readonly string Head = new('b', 40);
    private static readonly string Other = new('c', 40);
    private static readonly int? Done = 0;
    private static readonly int? Failed = 1;
    private static readonly RemoteTipCheck Push = new(Before: Old, Expected: Head);
    private static readonly RemoteTipCheck Delete = new(Before: Old, Expected: null);
    private static readonly RemoteTipCheck Publish = new(Before: null, Expected: Head);

    [Fact]
    public void WhenThePushedCommitIsOnTheRemoteAndGitSucceeded_ShouldJudgeItArrived() =>
        Assert.Equal(RemoteTipVerdict.Arrived, Push.Judge(Head, Done, false));

    [Fact]
    public void WhenThePushedCommitIsOnTheRemoteThoughThePushWasCancelled_ShouldJudgeItArrived() =>
        Assert.Equal(RemoteTipVerdict.Arrived, Push.Judge(Head, null, true));

    [Fact]
    public void WhenTheDeletedBranchIsGoneThoughTheConnectionWasLost_ShouldJudgeItArrived() =>
        Assert.Equal(RemoteTipVerdict.Arrived, Delete.Judge(null, null, true));

    [Fact]
    public void WhenThePublishedCommitIsOnTheRemoteThoughGitFailed_ShouldJudgeItArrived() =>
        Assert.Equal(RemoteTipVerdict.Arrived, Publish.Judge(Head, Failed, false));

    // An unchanged remote after an interruption means nothing was sent.
    [Fact]
    public void WhenAPushIsCancelledAndTheRemoteIsUnchanged_ShouldJudgeItInterrupted() =>
        Assert.Equal(RemoteTipVerdict.Interrupted, Push.Judge(Old, null, true));

    [Fact]
    public void WhenADeletionIsCancelledAndTheBranchIsStillThere_ShouldJudgeItInterrupted() =>
        Assert.Equal(RemoteTipVerdict.Interrupted, Delete.Judge(Old, null, true));

    [Fact]
    public void WhenAPublishIsCancelledAndTheRemoteStillHasNoBranch_ShouldJudgeItInterrupted() =>
        Assert.Equal(RemoteTipVerdict.Interrupted, Publish.Judge(null, null, true));

    [Fact]
    public void WhenGitFailsAPushAndTheRemoteIsUnchanged_ShouldJudgeItRefused() =>
        Assert.Equal(RemoteTipVerdict.Refused, Push.Judge(Old, Failed, false));

    [Fact]
    public void WhenGitFailsADeletionAndTheBranchIsStillThere_ShouldJudgeItRefused() =>
        Assert.Equal(RemoteTipVerdict.Refused, Delete.Judge(Old, Failed, false));

    [Fact]
    public void WhenGitFailsAPublishAndTheRemoteStillHasNoBranch_ShouldJudgeItRefused() =>
        Assert.Equal(RemoteTipVerdict.Refused, Publish.Judge(null, Failed, false));

    // A remote that holds something else can only be explained by a fetch.
    [Fact]
    public void WhenThePushedBranchHoldsAnotherCommit_ShouldJudgeItChanged() =>
        Assert.Equal(RemoteTipVerdict.Changed, Push.Judge(Other, Done, false));

    [Fact]
    public void WhenThePushedBranchIsGoneAfterGitFailed_ShouldJudgeItChanged() =>
        Assert.Equal(RemoteTipVerdict.Changed, Push.Judge(null, Failed, false));

    [Fact]
    public void WhenTheBranchToDeleteHoldsAnotherCommitAfterACancel_ShouldJudgeItChanged() =>
        Assert.Equal(RemoteTipVerdict.Changed, Delete.Judge(Other, null, true));

    [Fact]
    public void WhenAnotherCommitIsWhereAPublishFoundNothing_ShouldJudgeItChanged() =>
        // A publish expected nothing on the remote, so any other commit there belongs to someone else.
        Assert.Equal(RemoteTipVerdict.Changed, Publish.Judge(Other, Failed, false));

    // Git reporting success over an unchanged remote is not an answer Trag trusts.
    [Fact]
    public void WhenGitSaysAPushSucceededButTheRemoteIsUnchanged_ShouldJudgeItChanged() =>
        Assert.Equal(RemoteTipVerdict.Changed, Push.Judge(Old, Done, false));

    [Fact]
    public void WhenGitSaysADeletionSucceededButTheBranchIsStillThere_ShouldJudgeItChanged() =>
        Assert.Equal(RemoteTipVerdict.Changed, Delete.Judge(Old, Done, false));
}
