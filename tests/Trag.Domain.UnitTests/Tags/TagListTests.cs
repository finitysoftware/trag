using Trag.Domain.Presentation.Text;
using Trag.Domain.Tags;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Tags;

// The tags as the sidebar shows them: the button names the first and counts them once there is more than one, and a
// search keeps the tags whose name holds the text in any case.
public sealed class TagListTests
{
    private static readonly TagInfo[] Tags = [new("v1.10", Snapshots.HeadId, "Release ten"), new("v1.9", Snapshots.Id('b'), "Nine"), new("light", Snapshots.Id('c'), "Light")];

    [Fact]
    public void WhenTagsAreListed_ShouldNameTheFirstAndCountThemOnceThereIsMoreThanOne()
    {
        Assert.Equal(("v1.10", "3"), (TagList.ButtonText(Tags), TagList.CountText(Tags)));
        Assert.Equal(("v1.9", ""), (TagList.ButtonText([Tags[1]]), TagList.CountText([Tags[1]])));
        Assert.Equal((TagText.Button, ""), (TagList.ButtonText([]), TagList.CountText([])));
    }

    [Fact]
    public void WhenTagsAreSearched_ShouldKeepThoseWhoseNameHoldsTheTrimmedTextInAnyCase()
    {
        Assert.Equal(["v1.10", "v1.9"], TagList.Filter(Tags, " V1. ").Select(tag => tag.Name));
        Assert.Same(Tags, TagList.Filter(Tags, " "));
    }
}
