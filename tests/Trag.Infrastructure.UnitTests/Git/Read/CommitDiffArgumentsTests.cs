using Trag.Domain.History;
using Trag.Infrastructure.Git.Read;

namespace Trag.Infrastructure.UnitTests.Git.Read;

// What a commit is compared against. The file list and a single file's diff build their commands here, so they can
// never disagree about which parent the change is measured from.
public sealed class CommitDiffArgumentsTests
{
    private const string Head = "1111111111111111111111111111111111111111";
    private const string Parent = "2222222222222222222222222222222222222222";
    private static CommitDetails Commit(params string[] parents) =>
        new(new(Head, "Author", "author@example.invalid", "2024-01-01T00:00:00+00:00", "Subject") { Parents = parents }, "Subject", []);

    [Fact]
    public void WhenTheCommitIsARoot_ShouldCompareItWithAnEmptyTree() =>
        Assert.Equal(["diff-tree", "-r", "--no-commit-id", "--no-ext-diff", "--no-textconv", "--no-color", "--ignore-submodules=none", "--raw", "--root", Head, "--"],
            CommitDiffArguments.For(Commit(), ["--raw"]));

    [Fact]
    public void WhenTheCommitIsAMerge_ShouldCompareItWithItsFirstParent() =>
        Assert.Equal([Parent, Head, "--"], CommitDiffArguments.For(Commit(Parent, "3333333333333333333333333333333333333333"), []).TakeLast(3));

    [Fact]
    public void WhenPathsAreGiven_ShouldPutThemLastOnlyAfterTheSeparatorAndKeepTheOptionsInPlace()
    {
        var arguments = CommitDiffArguments.For(Commit(Parent), ["--patch", "--unified=3"], ["old č.txt", "new č.txt"]);
        Assert.Equal(["--", "old č.txt", "new č.txt"], arguments.TakeLast(3));
        Assert.Equal(["--patch", "--unified=3"], arguments.Skip(7).Take(2));
    }
}
