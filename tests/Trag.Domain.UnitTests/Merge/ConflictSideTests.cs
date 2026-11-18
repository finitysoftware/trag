using Trag.Domain.Merge;

namespace Trag.Domain.UnitTests.Merge;

// The conflict view names each action on a side by the word its sentences use for that side.
public sealed class ConflictSideTests
{
    [Fact]
    public void WhenASideIsOffered_ShouldNameTheWholeFileAndTheBlockActionsByItsWord()
    {
        var side = new ConflictSide("mine", "main", "main", ConflictChoice.Mine);
        Assert.Equal(("Use mine", "Take mine for the whole file, and stage it", "Take mine"), (side.UseAction, side.UseTip, side.TakeAction));
    }
}
