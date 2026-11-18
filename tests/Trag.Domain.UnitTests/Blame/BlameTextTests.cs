using Trag.Domain.Blame;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Blame;

public sealed class BlameTextTests
{
    [Fact]
    public void WhenTheVersionIsNamed_ShouldSayTheWorkingTreeOrTheCommitsShortId() =>
        Assert.Equal(("in the working tree", "at " + Snapshots.Id('a')[..7]), (BlameText.Version(null), BlameText.Version(Snapshots.Id('a'))));

    [Fact]
    public void WhenACommitIsSummed_ShouldNameItByShortIdAndSubjectOrSayItIsNotCommitted()
    {
        var commit = new BlameCommit(Snapshots.Id('b'), "Ana", DateTimeOffset.UnixEpoch, "Change a", Snapshots.Path("a.txt"));

        Assert.Equal(Snapshots.Id('b')[..7] + " · Change a", BlameText.Summary(commit));
        Assert.Equal(BlameText.NotCommitted, BlameText.Summary(commit with { Id = new string('0', 40) }));
    }
}
