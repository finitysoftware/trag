using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;

namespace Trag.Domain.UnitTests.Presentation.Text;

// What the dialog for editing the current branch's commits says: what it edits, what each choice does, and what the
// upstream already has.
public sealed class RewriteTextTests
{
    [Theory]
    [InlineData(1, "1 commit: abc1234")]
    [InlineData(3, "3 commits, from abc1234 to the tip")]
    public void WhenTheCommitsToEditAreNamed_ShouldCountThemFromTheChosenOne(int count, string subtitle) =>
        Assert.Equal(subtitle, RewriteText.Subtitle(count, "abc1234"));

    [Theory]
    [InlineData(RewriteAction.Pick, "Pick", "Keep the commit as it is")]
    [InlineData(RewriteAction.Reword, "Reword", "Keep the commit with a new message")]
    [InlineData(RewriteAction.Squash, "Squash", "Join it into the commit above, messages together")]
    [InlineData(RewriteAction.Fixup, "Fixup", "Join it into the commit above, dropping its message")]
    [InlineData(RewriteAction.Drop, "Drop", "Leave the commit out")]
    public void WhenAChoiceIsOffered_ShouldNameItAndSayWhatItDoes(RewriteAction action, string name, string hint) =>
        Assert.Equal((name, hint), (RewriteText.Name(action), RewriteText.Hint(action)));

    [Theory]
    [InlineData(1, "1 of these commits is already on origin/main; after the edit, a push will have to replace it there.")]
    [InlineData(2, "2 of these commits are already on origin/main; after the edit, a push will have to replace them there.")]
    public void WhenTheUpstreamHasSomeOfTheCommits_ShouldWarnThatAPushReplacesThem(int count, string warning) =>
        Assert.Equal(warning, RewriteText.Pushed(count, "origin/main"));
}
