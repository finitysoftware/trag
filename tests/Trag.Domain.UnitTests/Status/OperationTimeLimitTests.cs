using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Status;

public sealed class OperationTimeLimitTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(2);

    // A merge, rebase or pick that may wait for a signing key's passphrase, or write the content of Git LFS files, takes
    // as long as it takes; any other has the limit.
    [Fact]
    public void WhenAnOperationMayTakeLongForGoodReason_ShouldHaveNoLimit()
    {
        var plain = Snapshots.Create();

        Assert.Equal<TimeSpan?>(Limit, OperationTimeLimit.Of(plain, Limit));
        Assert.Null(OperationTimeLimit.Of(plain with { Signing = CommitSigning.OpenPgp }, Limit));
        Assert.Null(OperationTimeLimit.Of(plain with { HasLfsFiles = true }, Limit));
    }
}
