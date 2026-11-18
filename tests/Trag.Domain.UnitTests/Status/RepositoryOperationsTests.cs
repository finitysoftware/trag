using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Status;

// Conflicted entries left in the index without an operation in progress are a state of their own; during an operation
// they are that operation's.
public sealed class RepositoryOperationsTests
{
    private static readonly FileStatus[] Conflicted = [Snapshots.Modified("a.txt") with { IsConflict = true }];

    [Fact]
    public void WhenNoOperationRunsButTheIndexHoldsAConflict_ShouldBeTheConflictState() =>
        Assert.Equal(RepositoryOperation.Conflict, RepositoryOperations.With(RepositoryOperation.None, Conflicted));

    [Fact]
    public void WhenNoOperationRunsAndNothingIsConflicted_ShouldBeNone() =>
        Assert.Equal(RepositoryOperation.None, RepositoryOperations.With(RepositoryOperation.None, [Snapshots.Modified("a.txt")]));

    [Fact]
    public void WhenAMergeLeftAConflict_ShouldStayTheMerge() =>
        Assert.Equal(RepositoryOperation.Merge, RepositoryOperations.With(RepositoryOperation.Merge, Conflicted));
}
