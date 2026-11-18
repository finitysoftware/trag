using Trag.Domain.Naming;

namespace Trag.Domain.UnitTests.Naming;

public sealed class TagNamesTests
{
    [Theory]
    [InlineData("v 1", "Tag names cannot contain spaces.")]
    [InlineData("-v1", "A tag name cannot start with '-' or '/', or end with '/', '.' or '.lock'.")]
    [InlineData("v1.lock", "A tag name cannot start with '-' or '/', or end with '/', '.' or '.lock'.")]
    [InlineData("v1..2", "Tag names cannot contain '..', '~', '^', ':', '?', '*', '[', '\\' or '@{'.")]
    [InlineData("v1^", "Tag names cannot contain '..', '~', '^', ':', '?', '*', '[', '\\' or '@{'.")]
    [InlineData("release/.v1", "Parts of a tag name cannot start with '.' or end with '.lock'.")]
    [InlineData("release.lock/v1", "Parts of a tag name cannot start with '.' or end with '.lock'.")]
    [InlineData("release/nul", "Parts of a tag name cannot be a name Windows keeps for a device, such as CON, NUL or COM1.")]
    [InlineData("v1./x", "Parts of a tag name cannot end with '.' on Windows.")]
    public void WhenATagNameBreaksARuleOfGit_ShouldSayWhichOneForATag(string name, string error)
    {
        Assert.Equal(error, TagNames.Error(name));
        Assert.False(TagNames.IsValid(name));
    }

    [Fact]
    public void WhenATagNameIsLongerThanGitAllows_ShouldSayHowLongItMayBe() =>
        Assert.Equal("Tag names can have at most 255 characters.", TagNames.Error(new string('v', 256)));

    [Theory]
    [InlineData("v1.2.0")]
    [InlineData("release/2026-10")]
    [InlineData(" v2 ")]
    public void WhenATagNameFollowsGitsRules_ShouldBeValid(string name)
    {
        Assert.Equal("", TagNames.Error(name));
        Assert.True(TagNames.IsValid(name));
    }

    // The dialog opens empty: nothing is wrong yet, but there is nothing to create either.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenATagNameIsBlank_ShouldShowNoErrorButBeInvalid(string name)
    {
        Assert.Equal("", TagNames.Error(name));
        Assert.False(TagNames.IsValid(name));
    }
}
