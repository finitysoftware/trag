using Trag.Domain.Naming;
using Trag.Domain.Status;

namespace Trag.Application.UnitTests.Naming;

public sealed class BranchNamesTests
{
    [Fact]
    public void WhenTheSubjectHasLettersOutsideAscii_ShouldSuggestTheirAsciiForm() =>
        Assert.Equal("ispravka-citanja-dz", BranchNames.Suggest("Ispravka čitanja: Đž!", "1234567"));

    [Fact]
    public void WhenTheSubjectHasSymbols_ShouldSuggestOnlyItsWordsAndNumbers() => Assert.Equal("fix-42", BranchNames.Suggest("Fix #42", "abcdef0"));

    [Theory]
    [InlineData("feature/topic", "")]
    [InlineData("", "")]
    [InlineData("bad name", "spaces")]
    [InlineData("-lead", "cannot start")]
    [InlineData("trail/", "cannot start")]
    [InlineData("ends.lock", "cannot start")]
    [InlineData("a..b", "cannot contain")]
    [InlineData("a@{b", "cannot contain")]
    [InlineData("a/.hidden", "start with '.'")]
    [InlineData(".hidden/x", "start with '.'")]
    [InlineData("feature.lock/x", "or end with '.lock'")]
    [InlineData("v1.2.x", "")]
    [InlineData("修正/ветка-ü", "")]
    [InlineData("main", "already exists")]
    public void WhenANameIsChecked_ShouldNameTheFirstRuleItBreaks(string name, string expected)
    {
        var error = BranchNames.Error(name, ["main", "topic"]);
        if (expected.Length == 0) Assert.Empty(error);
        else Assert.Contains(expected, error);
    }

    // Windows keeps a branch as a file under refs/heads, and a file cannot be named after a device or end with a dot, so
    // Git accepts these names and then cannot write them.
    [Theory]
    [InlineData("aux")]
    [InlineData("feature/NUL")]
    [InlineData("con.x.y")]
    [InlineData("tools/com1")]
    [InlineData("lpt9/x")]
    [InlineData("conin$")]
    [InlineData("v1./x")]
    public void WhenWindowsCannotKeepTheName_ShouldSaySoBeforeGitIsAsked(string name) =>
        Assert.NotEmpty(BranchNames.Error(name, []));

    [Theory]
    [InlineData("auxiliary")]
    [InlineData("console")]
    [InlineData("com10")]
    [InlineData("feature/nul-check")]
    [InlineData("v1.x/y")]
    public void WhenANameOnlyLooksLikeOneWindowsReserves_ShouldBeFree(string name) => Assert.Empty(BranchNames.Error(name, []));

    [Fact]
    public void WhenTheNameIsOnlySpaces_ShouldBeInvalid() => Assert.False(BranchNames.IsValid("  ", []));

    [Fact]
    public void WhenTheNameHasSpacesAroundIt_ShouldBeValid() => Assert.True(BranchNames.IsValid(" ok ", []));

    [Fact]
    public void WhenTheNameIsAtTheLengthLimit_ShouldBreakNoRule() => Assert.Empty(BranchNames.Error(new string('a', GitLimits.NameLength), []));

    [Fact]
    public void WhenTheNameIsOverTheLengthLimit_ShouldNameTheLimit() =>
        Assert.Contains("255", BranchNames.Error(new string('a', GitLimits.NameLength + 1), []));

    // Windows keeps a branch as a file whose name ignores case, so Git takes Main for main.
    [Fact]
    public void WhenTheNameDiffersFromATakenOneOnlyInCase_ShouldBeTaken()
    {
        Assert.Equal("A branch with this name already exists.", BranchNames.Error("Main", ["main"]));
        Assert.Equal("Topic-2", BranchNames.UniqueName("Topic", ["topic"]));
    }

    // Git keeps a branch as a file under refs/heads, so topic and topic/x cannot both be branches: topic would be a file
    // and a folder at once. Windows ignores case there too.
    [Theory]
    [InlineData("topic/x", "topic")]
    [InlineData("Topic/y", "topic")]
    [InlineData("feat", "feat/a")]
    public void WhenTheNameHoldsOrSitsInsideAnotherBranch_ShouldNameTheBranchInTheWay(string name, string other) =>
        Assert.Equal("A branch cannot be named " + name + " while " + other + " exists.", BranchNames.Error(name, ["main", other]));

    // A rename moves the branch out of its own way first, so its old name never stands in the way of a name inside it or
    // holding it; another branch still does.
    [Fact]
    public void WhenARenameMovesTheBranchIntoOrOutOfItsOwnName_ShouldAllowIt()
    {
        Assert.Equal("", BranchNames.RenameError("topic/z", "topic", ["main", "topic"]));
        Assert.Equal("", BranchNames.RenameError("feat", "feat/a", ["main", "feat/a"]));
        Assert.Equal("A branch cannot be named topic/z while topic exists.", BranchNames.RenameError("topic/z", "main", ["main", "topic"]));
        Assert.True(BranchNames.IsValidRename("topic/z", "topic", ["main", "topic"]));
    }

    // Git refuses a rename that only changes the case while the branch is a loose ref and takes it once the branch is
    // packed, so the dialog refuses it every time: the old name counts as taken here.
    [Fact]
    public void WhenARenameOnlyChangesTheCase_ShouldRefuseItAsTaken() =>
        Assert.Equal("A branch with this name already exists.", BranchNames.RenameError("Topic", "topic", ["main", "topic"]));

    [Fact]
    public void WhenTheNameIsFree_ShouldKeepIt() => Assert.Equal("topic", BranchNames.UniqueName("topic", ["main"]));

    [Fact]
    public void WhenTheNameIsTaken_ShouldCountUpFromTwoToTheFirstFreeOne() => Assert.Equal("topic-3", BranchNames.UniqueName("topic", ["topic", "topic-2"]));

    [Fact]
    public void WhenHeadIsDetached_ShouldBlockSyncButNotFileWrites()
    {
        var detached = Detached();
        Assert.True(detached.IsDetached); Assert.True(detached.HasUnsupportedState); Assert.False(detached.HasUnsupportedWriteState); Assert.Equal(2, detached.DetachedCommits);
    }

    [Fact]
    public void WhenAMergeIsInProgressOnADetachedHead_ShouldBlockFileWrites()
    {
        var detached = Detached();
        Assert.True((detached with { Operation = RepositoryOperation.Merge }).HasUnsupportedWriteState);
    }

    [Fact]
    public void WhenHeadIsBackOnABranch_ShouldNotBlockSync()
    {
        var detached = Detached();
        Assert.False((detached with { Branch = "main", DetachedCommits = 0 }).HasUnsupportedState);
    }

    [Fact]
    public void WhenABranchIsSuggestedForACommit_ShouldNameItAfterTheSubject()
    {
        var head = new string('a', 40);
        Assert.Equal("fix-the-parser", BranchNames.SuggestFor("Fix the parser", head, []));
    }

    [Fact]
    public void WhenTheNameSuggestedForACommitIsTaken_ShouldCountUp()
    {
        var head = new string('a', 40);
        Assert.Equal("fix-the-parser-2", BranchNames.SuggestFor("Fix the parser", head, ["fix-the-parser"]));
    }

    [Fact]
    public void WhenTheCommitsSubjectHasNoWord_ShouldNameTheBranchAfterTheShortId()
    {
        var head = new string('a', 40);
        Assert.Equal("from-aaaaaaa", BranchNames.SuggestFor("!!!", head, []));
    }

    [Fact]
    public void WhenADetachedHeadIsKept_ShouldNameTheBranchAfterItsShortId()
    {
        var head = new string('a', 40);
        Assert.Equal("kept-from-aaaaaaa", BranchNames.SuggestKept(head, []));
    }

    [Fact]
    public void WhenTheNameForAKeptHeadIsTaken_ShouldCountUp()
    {
        var head = new string('a', 40);
        Assert.Equal("kept-from-aaaaaaa-2", BranchNames.SuggestKept(head, ["kept-from-aaaaaaa"]));
    }

    // HEAD on no branch, with 2 commits only it holds.
    private static RepositorySnapshot Detached() =>
        new("r", "2.43.0", null, new string('a', 40), null, null, [], RepositoryOperation.None, [], []) { DetachedCommits = 2 };
}
