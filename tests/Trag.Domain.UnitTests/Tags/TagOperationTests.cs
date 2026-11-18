using Trag.Domain.Tags;

namespace Trag.Domain.UnitTests.Tags;

// A repository that signs its tags makes annotated ones only: the dialog and the validator ask the same rule.
public sealed class TagOperationTests
{
    [Theory]
    [InlineData(true, "", true)]
    [InlineData(true, "  ", true)]
    [InlineData(true, "Release", false)]
    [InlineData(false, "", false)]
    public void WhenTheRepositorySignsItsTagsOrNot_ShouldSayWhetherATagLacksItsMessage(bool signsTags, string message, bool lacks) =>
        Assert.Equal(lacks, TagOperation.LacksMessage(signsTags, message));
}
