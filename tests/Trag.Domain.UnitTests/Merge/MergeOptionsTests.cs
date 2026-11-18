using Trag.Domain.Branches;
using Trag.Domain.Merge;
using Trag.Domain.Naming;
using Trag.Domain.Rebase;

namespace Trag.Domain.UnitTests.Merge;

// What the merge and rebase dialogs offer, and the merge message that follows the branches.
public sealed class MergeOptionsTests
{
    private static BranchInfo Local(string name) => new(name, "refs/heads/" + name, "h-" + name, false);
    private static BranchInfo Remote(string name) => new(name, "refs/remotes/" + name, "h-" + name, true, name.Split('/')[0]);

    private static readonly BranchCatalog Catalog = new(Local("main"), [Local("topic"), Local("fix")],
        [new("origin", [Remote("origin/main"), Remote("origin/topic")]), new("backup", [Remote("backup/main")])]);

    [Fact]
    public void WhenMergeTargetsAreOffered_ShouldOfferOnlyLocalBranchesTheCurrentOneFirst()
    {
        Assert.Equal(["main", "topic", "fix"], MergeOptions.Into(Catalog).Select(branch => branch.Name));
    }

    [Fact]
    public void WhenBranchesToMergeAreOffered_ShouldOfferEveryBranchInTheListsOrder()
    {
        Assert.Equal(["main", "topic", "fix", "origin/main", "origin/topic", "backup/main"], MergeOptions.From(Catalog).Select(branch => branch.Name));
    }

    [Fact]
    public void WhenHeadIsDetached_ShouldOfferTheOtherLocalBranchesAsMergeTargets()
    {
        var detached = Catalog with { Current = null };
        Assert.Equal(["topic", "fix"], MergeOptions.Into(detached).Select(branch => branch.Name));
    }

    [Fact]
    public void WhenRebaseTargetsAreOffered_ShouldOfferAnyOtherBranchLocalFirst()
    {
        Assert.Equal(["topic", "fix", "origin/main", "origin/topic", "backup/main"], RebaseOptions.Onto(Catalog).Select(branch => branch.Name));
    }

    [Fact]
    public void WhenTheMessageIsStillTheOneWrittenForThePreviousBranches_ShouldFollowTheNewOnes()
    {
        Assert.Equal("Merge topic into main", MergeMessages.Following("Merge fix into main", "Merge fix into main", "Merge topic into main"));
    }

    [Fact]
    public void WhenTheUserWroteTheMessage_ShouldKeepIt()
    {
        Assert.Equal("My own words", MergeMessages.Following("My own words", "Merge fix into main", "Merge topic into main"));
    }

    [Fact]
    public void WhenTheMessageIsEmptyBeforeAnyBranchWasChosen_ShouldFollowTheBranches()
    {
        Assert.Equal("Merge topic into main", MergeMessages.Following("", "", "Merge topic into main"));
    }
}
