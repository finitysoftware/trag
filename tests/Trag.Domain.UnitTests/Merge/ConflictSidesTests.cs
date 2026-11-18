using Trag.Domain.Merge;

namespace Trag.Domain.UnitTests.Merge;

// A conflict block's cards show the user's own work first, whichever side Git calls it: ours in a merge, theirs in a
// rebase or an applied stash.
public sealed class ConflictSidesTests
{
    private static readonly ConflictPart Block = new([], false, 0, Mine: [new(1, "git's ours")], Theirs: [new(1, "git's theirs")]);

    [Theory]
    [InlineData(ConflictChoice.Mine, "git's ours", "git's theirs")]
    [InlineData(ConflictChoice.Theirs, "git's theirs", "git's ours")]
    public void WhenABlockIsShown_ShouldPutTheUsersSideFirst(ConflictChoice yours, string first, string second)
    {
        var other = yours == ConflictChoice.Mine ? ConflictChoice.Theirs : ConflictChoice.Mine;
        var sides = new ConflictSides(new("yours", "feature", "feature", yours), new("other", "main", "main", other), "the change", "main");

        var (shownFirst, shownSecond) = sides.Arrange(Block);

        Assert.Equal((first, second), (Assert.Single(shownFirst).Text, Assert.Single(shownSecond).Text));
    }
}
