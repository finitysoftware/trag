using System.Text;
using Trag.Domain.Naming;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.UnitTests.Git.State;

// IndexTransaction, MergeMetadataReader and GitTransferProgress are internal; the test assembly is a friend.
public sealed class GitStateHelperTests : IDisposable
{
    private readonly string gitDirectory = Path.Combine(Path.GetTempPath(), "trag-state-" + Guid.NewGuid().ToString("N"));
    public GitStateHelperTests() => Directory.CreateDirectory(gitDirectory);
    public void Dispose() { if (Directory.Exists(gitDirectory)) Directory.Delete(gitDirectory, true); }
    private string File(string name) => Path.Combine(gitDirectory, name);
    private readonly string incoming = new('b', 40);
    private readonly string original = new('a', 40);

    [Fact]
    public async Task WhenATransactionBegins_ShouldTakeTheIndexLockAndRefuseASecondTransaction()
    {
        await System.IO.File.WriteAllBytesAsync(File("index"), [1, 2, 3]);
        await using var transaction = new IndexTransaction(gitDirectory);
        Assert.True(System.IO.File.Exists(File("index.lock")));
        Assert.Throws<IOException>(() => new IndexTransaction(gitDirectory));
    }

    [Fact]
    public async Task WhenTheIndexIsCopied_ShouldWriteItsBytesToATemporaryIndexOfTragsOwn()
    {
        await using var transaction = await BegunAsync();
        await transaction.CopyAsync(CancellationToken.None);
        Assert.Equal(new byte[] { 1, 2, 3 }, await System.IO.File.ReadAllBytesAsync(transaction.TemporaryIndex));
        Assert.StartsWith("trag-write-", Path.GetFileName(transaction.TemporaryIndex));
    }

    [Fact]
    public async Task WhenTheTransactionIsPublished_ShouldReplaceTheIndexAndReleaseTheLock()
    {
        await using var transaction = await BegunAsync();
        await transaction.CopyAsync(CancellationToken.None);
        await System.IO.File.WriteAllBytesAsync(transaction.TemporaryIndex, [9, 9]);
        await transaction.PublishAsync(CancellationToken.None);
        Assert.Equal(new byte[] { 9, 9 }, await System.IO.File.ReadAllBytesAsync(File("index")));
        Assert.False(System.IO.File.Exists(File("index.lock")));
    }

    [Fact]
    public async Task WhenAPublishedTransactionIsDisposed_ShouldRemoveItsTemporaryIndex()
    {
        var transaction = await PublishedAsync();
        await transaction.DisposeAsync();
        Assert.False(System.IO.File.Exists(transaction.TemporaryIndex));
    }

    [Fact]
    public async Task WhenAnUnpublishedTransactionIsDisposed_ShouldRemoveOnlyItsOwnFiles()
    {
        await System.IO.File.WriteAllBytesAsync(File("index"), [1]);
        var discarded = new IndexTransaction(gitDirectory);
        await discarded.CopyAsync(CancellationToken.None);
        await discarded.DisposeAsync();
        Assert.False(System.IO.File.Exists(File("index.lock"))); Assert.False(System.IO.File.Exists(discarded.TemporaryIndex));
        Assert.Equal(new byte[] { 1 }, await System.IO.File.ReadAllBytesAsync(File("index")));
    }

    [Fact]
    public async Task WhenATransactionIsKeptForReview_ShouldReportAnUncertainWriteThatNamesTheRecoveryIndex()
    {
        await DiscardedAsync();
        await using var preserved = new IndexTransaction(gitDirectory);
        var result = preserved.KeepForReview("Review the commit.", "commit-id", "Git details\n");
        Assert.Equal(GitWriteOutcome.Uncertain, result.Outcome);
        Assert.Equal("commit-id", result.CommitId);
        Assert.Equal("Git details\nRecovery index: " + preserved.TemporaryIndex, result.Details);
    }

    [Fact]
    public async Task WhenATransactionKeptForReviewIsDisposed_ShouldReleaseTheLockButKeepTheTemporaryIndex()
    {
        await DiscardedAsync();
        var preserved = new IndexTransaction(gitDirectory);
        preserved.KeepForReview("Review the commit.", "commit-id", "Git details\n");
        await preserved.CopyAsync(CancellationToken.None);
        await preserved.DisposeAsync();
        Assert.False(System.IO.File.Exists(File("index.lock"))); Assert.True(System.IO.File.Exists(preserved.TemporaryIndex));
    }

    // The writers ask through TryBegin: a foreign lock is null and stays.
    [Fact]
    public async Task WhenAnotherOperationHoldsTheIndexLock_ShouldRefuseTheTransactionAndNeverDeleteTheLock()
    {
        await System.IO.File.WriteAllTextAsync(File("index.lock"), "someone else");
        Assert.Throws<IOException>(() => new IndexTransaction(gitDirectory));
        Assert.Null(IndexTransaction.TryBegin(gitDirectory));
        Assert.Equal("someone else", await System.IO.File.ReadAllTextAsync(File("index.lock")));
    }

    // Only a foreign lock is an answer; any other failure to take the lock still throws.
    [Fact]
    public async Task WhenTheGitDirectoryDoesNotExist_ShouldThrowRatherThanReportAForeignLock()
    {
        await ForeignLockRefusedAsync();
        Assert.Throws<DirectoryNotFoundException>(() => IndexTransaction.TryBegin(File("missing")));
    }

    // Git creates the index itself on the first write.
    [Fact]
    public async Task WhenTheRepositoryHasNoIndexYet_ShouldCopyNothing()
    {
        await ForeignLockRefusedAsync();
        Assert.Throws<DirectoryNotFoundException>(() => IndexTransaction.TryBegin(File("missing")));
        var unborn = new IndexTransaction(Directory.CreateDirectory(File("unborn")).FullName);
        await unborn.CopyAsync(CancellationToken.None);
        Assert.False(System.IO.File.Exists(unborn.TemporaryIndex));
        await unborn.DisposeAsync();
    }

    [Fact]
    public async Task WhenNoMergeIsInProgress_ShouldReadNoMerge() => Assert.Null(await MergeMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None));

    [Fact]
    public async Task WhenAMergeStopsWithBothHeadsAndAMessage_ShouldReadThemWithoutALimitation()
    {
        var merge = await MergingAsync();
        Assert.Equal(incoming, merge.IncomingHead); Assert.Equal(original, merge.OriginalHead); Assert.Equal("Merge branch 'topic'", merge.Message); Assert.Equal("", merge.Limitation);
    }

    [Fact]
    public async Task WhenTheMergeMessageChanges_ShouldChangeTheFingerprint()
    {
        var merge = await MergingAsync();
        await System.IO.File.WriteAllTextAsync(File("MERGE_MSG"), "Merge branch 'other'\n");
        var changed = (await MergeMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None))!;
        Assert.NotEqual(merge.Fingerprint, changed.Fingerprint);
    }

    [Fact]
    public async Task WhenTheMergeUsesAnAutostash_ShouldNameTheAutostashAsItsLimitation()
    {
        await MessageChangedAsync();
        await System.IO.File.WriteAllTextAsync(File("MERGE_AUTOSTASH"), new string('c', 40));
        Assert.Contains("autostash", (await MergeMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None))!.Limitation);
    }

    [Fact]
    public async Task WhenTheOriginalHeadIsNoCommitId_ShouldDropItAndSendTheUserToTheirGitTools()
    {
        await AutostashedAsync();
        System.IO.File.Delete(File("MERGE_AUTOSTASH"));
        await System.IO.File.WriteAllTextAsync(File("ORIG_HEAD"), "junk\n");
        var invalid = (await MergeMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None))!;
        Assert.Null(invalid.OriginalHead); Assert.Equal(incoming, invalid.IncomingHead); Assert.Contains("Use your Git tools", invalid.Limitation);
    }

    // Git writes MERGE_MSG as long as the merge needs (a line per conflicted file): the merge stays readable, its message is
    // kept up to the largest one a commit takes, in whole lines, and the fingerprint still covers the bytes past that.
    [Fact]
    public async Task WhenTheMergeMessageIsLargerThanACommitTakes_ShouldKeepItsWholeLinesUpToThatSizeAndFingerprintTheRest()
    {
        await WithAnInvalidOriginalHeadAsync();
        var message = "Merge branch 'feature'\n\n# Conflicts:\n" + string.Concat(Enumerable.Range(0, 3000).Select(i => $"#\tsrc/module-{i:0000}/component.ts\n"));
        await System.IO.File.WriteAllTextAsync(File("MERGE_MSG"), message);

        var merge = (await MergeMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None))!;
        await System.IO.File.WriteAllTextAsync(File("MERGE_MSG"), message[..^2] + "X\n");
        var changed = (await MergeMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None))!;

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(merge.Message) <= GitLimits.CommitMessageBytes);
        Assert.StartsWith("Merge branch 'feature'\n\n# Conflicts:\n#\tsrc/module-0000/component.ts", merge.Message);
        Assert.EndsWith("/component.ts", merge.Message);
        Assert.Equal(merge.Message, changed.Message);
        Assert.NotEqual(merge.Fingerprint, changed.Fingerprint);
    }

    // A message in another encoding is read as it decodes, so the merge stays readable.
    [Fact]
    public async Task WhenTheMergeMessageIsNotUtf8_ShouldReadItAsItDecodes()
    {
        await WithAnInvalidOriginalHeadAsync();
        await System.IO.File.WriteAllBytesAsync(File("MERGE_MSG"), [0xFF, 0xFE, (byte)'x']);
        Assert.Equal("��x", (await MergeMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None))!.Message);
    }

    [Fact]
    public void WhenAKnownCounterArrives_ShouldReportItsLine()
    {
        var reports = new List<string>();
        AfterTheFirstCounter(reports);
        Assert.Equal("remote: Counting objects:  10% (1/10)", Assert.Single(reports));
    }

    [Fact]
    public void WhenAnotherCounterFollowsWithin100Milliseconds_ShouldNotReportIt()
    {
        var reports = new List<string>();
        var progress = AfterTheFirstCounter(reports);
        progress.Feed("Receiving objects:  20% (2/10)\r"u8);
        Assert.Single(reports);
    }

    [Fact]
    public void WhenAnUnknownLineArrivesBesideACounter_ShouldReportOnlyTheCounter()
    {
        var reports = new List<string>();
        var progress = AfterAThrottledCounter(reports);
        Thread.Sleep(150);
        progress.Feed("warning: something else\nReceiving objects:  30% (3/10)\n"u8);
        Assert.Equal("Receiving objects:  30% (3/10)", reports[^1]); Assert.Equal(2, reports.Count);
    }

    [Fact]
    public void WhenACounterLineIsTooLong_ShouldReportItCutTo512Characters()
    {
        var reports = new List<string>();
        var progress = AfterAWarningBesideACounter(reports);
        Thread.Sleep(150);
        progress.Feed(Encoding.ASCII.GetBytes("Resolving deltas: " + new string('x', 600) + "\n"));
        Assert.Equal(3, reports.Count); Assert.True(reports[^1].Length <= 512);
    }

    [Fact]
    public void WhenACounterCarriesNonAsciiBytes_ShouldReportItWithoutThem()
    {
        var reports = new List<string>();
        var progress = AfterALongCounter(reports);
        Thread.Sleep(150);
        progress.Feed([0xC4, 0x8D, (byte)'W', (byte)'r', (byte)'i', (byte)'t', (byte)'i', (byte)'n', (byte)'g', (byte)' ', (byte)'o', (byte)'b', (byte)'j', (byte)'e', (byte)'c', (byte)'t', (byte)'s', (byte)':', (byte)'\n']);
        Assert.Equal("Writing objects:", reports[^1]);
    }

    // The lock over a three-byte index is taken, and a second transaction has already been refused.
    private async Task<IndexTransaction> BegunAsync()
    {
        await System.IO.File.WriteAllBytesAsync(File("index"), [1, 2, 3]);
        var transaction = new IndexTransaction(gitDirectory);
        Assert.Throws<IOException>(() => new IndexTransaction(gitDirectory));
        return transaction;
    }

    // ...then copied, given two bytes of its own and published.
    private async Task<IndexTransaction> PublishedAsync()
    {
        var transaction = await BegunAsync();
        await transaction.CopyAsync(CancellationToken.None);
        await System.IO.File.WriteAllBytesAsync(transaction.TemporaryIndex, [9, 9]);
        await transaction.PublishAsync(CancellationToken.None);
        return transaction;
    }

    // A transaction over a one-byte index, copied and disposed without being published.
    private async Task DiscardedAsync()
    {
        await System.IO.File.WriteAllBytesAsync(File("index"), [1]);
        var discarded = new IndexTransaction(gitDirectory);
        await discarded.CopyAsync(CancellationToken.None);
        await discarded.DisposeAsync();
    }

    // Another operation's lock, which both ways of beginning have already met.
    private async Task ForeignLockRefusedAsync()
    {
        await System.IO.File.WriteAllTextAsync(File("index.lock"), "someone else");
        Assert.Throws<IOException>(() => new IndexTransaction(gitDirectory));
        Assert.Null(IndexTransaction.TryBegin(gitDirectory));
    }

    // A merge Git stopped with both heads and its message, read once before Git wrote anything and once after.
    private async Task<RepositoryMerge> MergingAsync()
    {
        await MergeMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None);
        await System.IO.File.WriteAllTextAsync(File("MERGE_HEAD"), incoming + "\n");
        await System.IO.File.WriteAllTextAsync(File("ORIG_HEAD"), original + "\n");
        await System.IO.File.WriteAllTextAsync(File("MERGE_MSG"), "Merge branch 'topic'\n");
        return (await MergeMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None))!;
    }

    // ...then its message changed and read again.
    private async Task MessageChangedAsync()
    {
        await MergingAsync();
        await System.IO.File.WriteAllTextAsync(File("MERGE_MSG"), "Merge branch 'other'\n");
        await MergeMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None);
    }

    // ...then an autostash added and read.
    private async Task AutostashedAsync()
    {
        await MessageChangedAsync();
        await System.IO.File.WriteAllTextAsync(File("MERGE_AUTOSTASH"), new string('c', 40));
        await MergeMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None);
    }

    // ...then the autostash gone, the original head made junk and read.
    private async Task WithAnInvalidOriginalHeadAsync()
    {
        await AutostashedAsync();
        System.IO.File.Delete(File("MERGE_AUTOSTASH"));
        await System.IO.File.WriteAllTextAsync(File("ORIG_HEAD"), "junk\n");
        await MergeMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None);
    }

    // The first counter is reported, so a next one within 100 ms falls in the throttle.
    private static GitTransferProgress AfterTheFirstCounter(List<string> reports)
    {
        var progress = new GitTransferProgress(new Sink(reports));
        progress.Feed("remote: Counting objects:  10% (1/10)\r"u8);
        return progress;
    }

    // ...then a counter inside the throttle.
    private static GitTransferProgress AfterAThrottledCounter(List<string> reports)
    {
        var progress = AfterTheFirstCounter(reports);
        progress.Feed("Receiving objects:  20% (2/10)\r"u8);
        return progress;
    }

    // ...then, past the throttle, a warning beside a counter.
    private static GitTransferProgress AfterAWarningBesideACounter(List<string> reports)
    {
        var progress = AfterAThrottledCounter(reports);
        Thread.Sleep(150);
        progress.Feed("warning: something else\nReceiving objects:  30% (3/10)\n"u8);
        return progress;
    }

    // ...then, past the throttle again, an over-long counter.
    private static GitTransferProgress AfterALongCounter(List<string> reports)
    {
        var progress = AfterAWarningBesideACounter(reports);
        Thread.Sleep(150);
        progress.Feed(Encoding.ASCII.GetBytes("Resolving deltas: " + new string('x', 600) + "\n"));
        return progress;
    }

    private sealed class Sink(List<string> reports) : IProgress<string>
    {
        public void Report(string value) => reports.Add(value);
    }
}
