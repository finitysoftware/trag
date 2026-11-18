using Trag.Domain.Naming;

namespace Trag.Domain.UnitTests.Naming;

public sealed class StashMessagesTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("half-done login form", true)]
    [InlineData("修复 登录 — ü 🚀", true)]
    [InlineData("-looks like an option", false)]
    [InlineData("  -indented option", false)]
    [InlineData("tab\there", false)]
    public void WhenAStashMessageIsChecked_ShouldBeValidOnlyWhenPrintableAndNotAnOption(string message, bool valid) => Assert.Equal(valid, StashMessages.IsValid(message));

    [Fact]
    public void WhenTheMessageIsAtTheGitLimit_ShouldBeValid() => Assert.True(StashMessages.IsValid(new string('a', GitLimits.StashMessageLength)));

    // The limit counts UTF-16 characters, so an emoji, two of them, that ends the message at the limit still fits.
    [Fact]
    public void WhenAnEmojiEndsTheMessageAtTheGitLimit_ShouldBeValid() =>
        Assert.True(StashMessages.IsValid(new string('a', GitLimits.StashMessageLength - 2) + "🚀"));

    [Fact]
    public void WhenTheMessageIsOverTheGitLimit_ShouldBeInvalid() => Assert.False(StashMessages.IsValid(new string('a', GitLimits.StashMessageLength + 1)));
}
