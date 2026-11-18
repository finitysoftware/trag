using Trag.Domain.Publish;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Write;

// Which requests leave the machine. The router sends exactly these three to the synchronizer and keeps the rest local;
// with no synchronizer it answers for them itself rather than reading a repository it is not going to write to.
public sealed class GitWriteRoutingTests
{

    public static TheoryData<GitWriteRequest> Remote => new()
    {
        new SyncRequest(Snapshots.Create(), FakeGitSync.DefaultState(Snapshots.Create(), "origin", null), SyncAction.Fetch),
        new PublishRequest(Snapshots.Create(), new(PublishKind.ExistingRemote, "origin", "main", true)),
        new RemoteBranchDeleteRequest(Snapshots.Create(), "origin", "topic"),
        new RemoteTagRequest(Snapshots.Create(), "origin", "v1.0", RemoteTagAction.Push)
    };

    public static TheoryData<GitWriteRequest> Local => new()
    {
        new StageRequest(Snapshots.Create(), [Snapshots.Path("one.txt")]),
        new CommitRequest(Snapshots.Create(), "message"),
        new BranchRequest(Snapshots.Create(), new("topic"), BranchAction.Delete)
    };

    [Theory]
    [MemberData(nameof(Remote))]
    public async Task WhenARemoteRequestHasNoSynchronizer_ShouldBeRejectedAsUnavailable(GitWriteRequest request)
    {
        var result = await new GitWriteRouter(new()).ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.Equal("Synchronization is unavailable.", result.Message);
    }

    [Theory]
    [MemberData(nameof(Local))]
    public async Task WhenALocalRequestHasNoSynchronizer_ShouldNeverBeRejectedAsUnavailable(GitWriteRequest request)
    {
        var result = await new GitWriteRouter(new()).ExecuteAsync(request, CancellationToken.None);

        Assert.NotEqual("Synchronization is unavailable.", result.Message);
    }

    // Deleting a local branch and deleting the same branch on a remote are two different requests, which is why the
    // router no longer has to look inside a payload to decide where to send one.
    [Fact]
    public void WhenALocalBranchIsDeleted_ShouldBeABranchRequest() =>
        Assert.IsType<BranchRequest>(new BranchRequest(Snapshots.Create(), new("topic"), BranchAction.Delete));

    [Fact]
    public void WhenTheSameBranchIsDeletedOnARemote_ShouldBeARemoteBranchDeleteRequest() =>
        Assert.IsType<RemoteBranchDeleteRequest>(new RemoteBranchDeleteRequest(Snapshots.Create(), "origin", "topic"));
}
