using Trag.Domain.Stash;

namespace Trag.Domain.UnitTests.Stash;

public sealed class StashEntryTests
{
    [Fact]
    public void WhenGitWroteTheSubject_ShouldReadTheBranchAndTitleOutOfIt()
    {
        var wip = new StashEntry(new string('a', 40), 0, "WIP on main: 1a2b3c4 Rename the window title", StashEntry.BranchOf("WIP on main: 1a2b3c4 Rename the window title"), DateTimeOffset.UnixEpoch);
        Assert.Equal("main", wip.Branch); Assert.Equal("1a2b3c4 Rename the window title", wip.Title); Assert.Equal("stash@{0}", wip.Reference);
    }

    [Fact]
    public void WhenTheUserNamedTheStash_ShouldReadTheBranchAndTheNameOutOfGitsSubject()
    {
        var named = Named();
        Assert.Equal("feature/login", named.Branch); Assert.Equal("half-done form", named.Title); Assert.Equal("stash@{2}", named.Reference);
    }

    [Fact]
    public void WhenTheSubjectIsNotGits_ShouldReadNoBranchAndTakeTheWholeSubjectAsTitle()
    {
        var named = Named();
        Assert.Null(StashEntry.BranchOf("something else")); Assert.Equal("something else", (named with { Message = "something else" }).Title);
    }

    [Fact]
    public void WhenTheStashWasMadeOnADetachedHead_ShouldReadTheBranchAsGitWroteIt()
    {
        Assert.Equal("(no branch)", StashEntry.BranchOf("WIP on (no branch): 1a2b3c4 detached work"));
    }

    [Fact]
    public void WhenAStashIsPreviewed_ShouldShowItAsItsCommit()
    {
        var when = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
        var commit = new StashEntry(new string('s', 40), 0, "On main: wip", "main", when).AsCommit();
        Assert.Equal(new string('s', 40), commit.Id); Assert.Equal("On main: wip", commit.Subject);
        Assert.Equal(when, DateTimeOffset.Parse(commit.Date, System.Globalization.CultureInfo.InvariantCulture));
    }

    // The third stash, which the user named on feature/login.
    private static StashEntry Named() =>
        new(new string('b', 40), 2, "On feature/login: half-done form", StashEntry.BranchOf("On feature/login: half-done form"), DateTimeOffset.UnixEpoch);
}
