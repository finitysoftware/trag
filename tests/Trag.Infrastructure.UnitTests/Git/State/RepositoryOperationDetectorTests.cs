using Trag.Domain.Status;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.UnitTests.Git.State;

// The markers Git leaves in its directory, read in the order the detector checks them.
public sealed class RepositoryOperationDetectorTests : IDisposable
{
    private readonly string gitDirectory = Path.Combine(Path.GetTempPath(), "trag-operation-" + Guid.NewGuid().ToString("N"));
    public RepositoryOperationDetectorTests() => Directory.CreateDirectory(gitDirectory);
    public void Dispose() { if (Directory.Exists(gitDirectory)) Directory.Delete(gitDirectory, true); }

    private RepositoryOperation DetectWithFile(string name)
    {
        File.WriteAllText(Path.Combine(gitDirectory, name), "marker");
        return RepositoryOperationDetector.Detect(gitDirectory, RootRefs.FromFiles(gitDirectory));
    }

    private RepositoryOperation DetectWithDirectory(params string[] names)
    {
        foreach (var name in names) Directory.CreateDirectory(Path.Combine(gitDirectory, Path.GetDirectoryName(name) is { Length: > 0 } parent ? parent : name));
        foreach (var name in names.Where(name => Path.GetDirectoryName(name) is { Length: > 0 })) File.WriteAllText(Path.Combine(gitDirectory, name), "marker");
        return RepositoryOperationDetector.Detect(gitDirectory, RootRefs.FromFiles(gitDirectory));
    }

    [Fact]
    public void WhenTheGitDirectoryHasNoMarker_ShouldReportNoOperation() => Assert.Equal(RepositoryOperation.None, RepositoryOperationDetector.Detect(gitDirectory, RootRefs.FromFiles(gitDirectory)));

    [Theory]
    [InlineData("CHERRY_PICK_HEAD", RepositoryOperation.CherryPick)]
    [InlineData("REVERT_HEAD", RepositoryOperation.Revert)]
    [InlineData("MERGE_HEAD", RepositoryOperation.Merge)]
    [InlineData("BISECT_START", RepositoryOperation.Bisect)]
    public void WhenAMarkerFileIsPresent_ShouldReportItsOperation(string name, RepositoryOperation expected) => Assert.Equal(expected, DetectWithFile(name));

    [Fact]
    public void WhenARebaseApplyDirectoryIsPresent_ShouldReportARebase() => Assert.Equal(RepositoryOperation.Rebase, DetectWithDirectory("rebase-apply"));

    [Fact]
    public void WhenTheRebaseApplyDirectoryIsApplyingAMailbox_ShouldReportApplyMailbox()
    {
        DetectWithDirectory("rebase-apply");
        Assert.Equal(RepositoryOperation.ApplyMailbox, DetectWithDirectory("rebase-apply/applying"));
    }

    // The mailbox being applied is still there; the merge directory is checked before it.
    [Fact]
    public void WhenARebaseMergeDirectoryAppearsBesideAMailbox_ShouldReportARebase()
    {
        DetectWithDirectory("rebase-apply");
        DetectWithDirectory("rebase-apply/applying");
        Assert.Equal(RepositoryOperation.Rebase, DetectWithDirectory("rebase-merge"));
    }

    [Fact]
    public void WhenASequencerRunLeavesAMergeHeadBehind_ShouldReportTheSequencerRatherThanAPlainMerge()
    {
        File.WriteAllText(Path.Combine(gitDirectory, "MERGE_HEAD"), "marker");
        RepositoryOperationDetector.Detect(gitDirectory, RootRefs.FromFiles(gitDirectory));
        Directory.CreateDirectory(Path.Combine(gitDirectory, "sequencer"));
        Assert.Equal(RepositoryOperation.Sequencer, RepositoryOperationDetector.Detect(gitDirectory, RootRefs.FromFiles(gitDirectory)));
    }

    // A cherry-pick or a revert of a range that stopped part way keeps Git's sequencer beside its pick head: Git's
    // --continue picks the rest and --abort undoes the commits already picked, so it is a Git sequence, not one pick.
    [Theory]
    [InlineData("CHERRY_PICK_HEAD")]
    [InlineData("REVERT_HEAD")]
    public void WhenAPickOfARangeStopsPartWay_ShouldReportTheSequencerRatherThanThePick(string name)
    {
        Directory.CreateDirectory(Path.Combine(gitDirectory, "sequencer"));
        Assert.Equal(RepositoryOperation.Sequencer, DetectWithFile(name));
    }
}
