using Trag.Domain.Presentation.Text;
using Trag.Domain.Stash;

namespace Trag.Domain.UnitTests.Stash;

// The sidebar's stash button names the newest entry and counts the entries once there is more than one.
public sealed class StashListTests
{
    private static StashEntry Entry(int index, string message) => new(new string((char)('a' + index), 40), index, message, StashEntry.BranchOf(message), DateTimeOffset.UnixEpoch);

    [Fact]
    public void WhenThereAreNoStashes_ShouldSayStashesAndCountNothing()
    {
        Assert.Equal((StashText.Button, ""), (StashList.ButtonText([]), StashList.CountText([])));
    }

    [Fact]
    public void WhenThereAreStashes_ShouldNameTheNewestAndCountThemFromTwo()
    {
        StashEntry[] one = [Entry(0, "On main: spike")];
        StashEntry[] two = [Entry(0, "On main: spike"), Entry(1, "WIP on main: 1a2b3c4 Older")];
        Assert.Equal(("spike", ""), (StashList.ButtonText(one), StashList.CountText(one)));
        Assert.Equal(("spike", "2"), (StashList.ButtonText(two), StashList.CountText(two)));
    }
}
