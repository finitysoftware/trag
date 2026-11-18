using Trag.Domain.Publish;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Publish;

public sealed class PublishOperationTests
{
    [Fact]
    public void WhenTheExistingRemoteBecameTheBranchsUpstream_ShouldBeConfigured()
    {
        var (snapshot, existing, _) = Operations();
        Assert.True(existing.IsConfiguredIn(snapshot));
    }

    [Fact]
    public void WhenTheBranchTracksNoRemote_ShouldNotBeConfiguredForTheExistingRemote()
    {
        var (snapshot, existing, _) = Operations();
        Assert.False(existing.IsConfiguredIn(snapshot with { TrackingRemote = null }));
    }

    [Fact]
    public void WhenNoSnapshotIsKnown_ShouldNotBeConfigured()
    {
        var (_, existing, _) = Operations();
        Assert.False(existing.IsConfiguredIn(null));
    }

    [Fact]
    public void WhenTheRemoteToAddIsNotThereYet_ShouldNotBeConfigured()
    {
        var (snapshot, _, added) = Operations();
        Assert.False(added.IsConfiguredIn(snapshot));
    }

    [Fact]
    public void WhenTheSnapshotRecordsTheAddedRemote_ShouldBeConfigured()
    {
        var (snapshot, _, added) = Operations();
        Assert.True(added.IsConfiguredIn(snapshot with { Remotes = [new("origin", "o", "o"), new("backup", "b", "b")] }));
    }

    [Fact]
    public void WhenThePublishSucceeded_ShouldBeDone()
    {
        var operation = AddUpstream();
        Assert.True(operation.IsDone(new(Trag.Domain.Writes.GitWriteOutcome.Succeeded, "Published.")));
    }

    // The remote was recorded, so only the push after it failed.
    [Fact]
    public void WhenThePublishFailedAfterRecordingItsRemote_ShouldBeDone()
    {
        var operation = AddUpstream();
        var recorded = Snapshots.Create(remotes: [Snapshots.Remote("upstream")]);
        Assert.True(operation.IsDone(new(Trag.Domain.Writes.GitWriteOutcome.Rejected, "Push failed.", Snapshot: recorded)));
    }

    [Fact]
    public void WhenThePublishFailedBeforeRecordingItsRemote_ShouldNotBeDone()
    {
        var operation = AddUpstream();
        Assert.False(operation.IsDone(new(Trag.Domain.Writes.GitWriteOutcome.Rejected, "Remote not added.", Snapshot: Snapshots.Create())));
    }

    // A snapshot whose branch tracks origin, a publish to origin, and a publish that adds backup.
    private static (Trag.Domain.Status.RepositorySnapshot Snapshot, PublishOperation Existing, PublishOperation Added) Operations()
    {
        var snapshot = Snapshots.Create(remotes: [new("origin", "o", "o")], trackingRemote: "origin");
        var existing = new PublishOperation(PublishKind.ExistingRemote, "origin", "main", Push: true);
        var added = new PublishOperation(PublishKind.AddRemote, "backup", "main", Push: false, RemoteUrl: "https://example.invalid/r.git");
        return (snapshot, existing, added);
    }

    private static PublishOperation AddUpstream() =>
        new(PublishKind.AddRemote, "upstream", "main", true, RemoteUrl: "https://example.invalid/r.git");
}
