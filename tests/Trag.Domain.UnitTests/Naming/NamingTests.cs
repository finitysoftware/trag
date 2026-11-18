using Trag.Domain.Errors;
using Trag.Domain.Naming;
using Trag.Domain.Sync;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Naming;

// Edge cases of the small naming and text helpers in Domain that the UI relies on.
public sealed class NamingTests
{
    [Theory]
    [InlineData("one two three four five six", "one-two-three-four-five-six")]
    [InlineData("one two three four five six seven", "one-two-three-four-five-six")]
    [InlineData("one two three four five six sev", "one-two-three-four-five-six")]
    [InlineData("Šđž", "sdz")]
    [InlineData("Ćevapčići đ", "cevapcici-d")]
    [InlineData("42", "42")]
    [InlineData("", "from-1234567")]
    [InlineData("   ", "from-1234567")]
    public void WhenABranchNameIsSuggestedFromASubject_ShouldStopAtSixWordsAndFoldDiacritics(string subject, string expected) => Assert.Equal(expected, BranchNames.Suggest(subject, "1234567"));

    [Fact]
    public void WhenAPublishNameHasOneHundredCharacters_ShouldBeAccepted() => Assert.Equal("", PublishNames.Error(new string('a', 100)));

    [Fact]
    public void WhenAPublishNameIsLongerThanOneHundredCharacters_ShouldShowTheHelp() => Assert.Equal(PublishNames.Help, PublishNames.Error(new string('a', 101)));

    [Fact]
    public void WhenAPublishNameHasASpace_ShouldShowTheHelp() => Assert.Equal(PublishNames.Help, PublishNames.Error("bad name"));

    [Fact]
    public void WhenAPublishNameHasAForbiddenStartOrEnd_ShouldSayWhatItCannotStartOrEndWith()
    {
        Assert.Contains("cannot start", PublishNames.Error("-lead"));
        Assert.Contains("cannot start", PublishNames.Error(".lead"));
        Assert.Contains("cannot start", PublishNames.Error("trail."));
        Assert.Contains("cannot start", PublishNames.Error("name.git"));
    }

    [Fact]
    public void WhenAPublishNameUsesLettersDigitsDashesUnderscoresAndDots_ShouldBeAccepted() => Assert.Equal("", PublishNames.Error("ok-name_1.2"));

    // A blank name is not wrong yet, so it shows no error, but there is nothing to publish.
    [Fact]
    public void WhenAPublishNameIsOnlySpaces_ShouldShowNoErrorButBeInvalid()
    {
        Assert.Equal("", PublishNames.Error("  "));
        Assert.False(PublishNames.IsValid("  "));
    }

    [Fact]
    public void WhenAPublishNameHasSpacesAroundIt_ShouldBeValid() => Assert.True(PublishNames.IsValid(" ok "));

    [Theory]
    [InlineData("My Repo!!", "My-Repo")]
    [InlineData("x.git", "x")]
    [InlineData("...", "repository")]
    [InlineData("a--b---c", "a-b-c")]
    public void WhenAPublishNameIsSuggestedFromTheFolder_ShouldSanitizeTheFolderName(string folder, string expected) => Assert.Equal(expected, PublishNames.Suggest(folder));

    [Fact]
    public void WhenTheShortenedSuggestionWouldEndInASeparator_ShouldDropTheSeparator()
    {
        var suggestion = PublishNames.Suggest(new string('a', 99) + "-" + new string('b', 50));
        Assert.Equal(99, suggestion.Length); Assert.DoesNotContain('-', suggestion);
    }

    [Fact]
    public void WhenTheFolderNameIsLongerThanOneHundredCharacters_ShouldShortenTheSuggestionToOneHundred() =>
        Assert.Equal(100, PublishNames.Suggest(new string('a', 120)).Length);

    [Fact]
    public void WhenACommitIdIsFull_ShouldShortenItToTenCharacters() => Assert.Equal("aaaaaaaaaa", Snapshots.Commit(Snapshots.HeadId, "x").ShortId);

    [Fact]
    public void WhenACommitIdIsShorterThanTen_ShouldKeepItWhole() => Assert.Equal("abc", Snapshots.Commit("abc", "x").ShortId);

    [Fact]
    public void WhenTheRefIsABranch_ShouldShowItWithoutTheHeadsPrefix() =>
        Assert.Equal("main", new SyncBranch("refs/heads/main", "refs/remotes/origin/main").Display);

    [Fact]
    public void WhenTheRefIsNotABranch_ShouldKeepTheWholeName() =>
        Assert.Equal("refs/tags/v1", new SyncBranch("refs/tags/v1", "refs/remotes/origin/v1").Display);

    [Fact]
    public void WhenARepositoryExceptionIsThrown_ShouldCarryItsErrorCodeAlsoAsTheMessage()
    {
        var error = new RepositoryException(RepositoryError.GitTooOld);
        Assert.Equal(RepositoryError.GitTooOld, error.Error); Assert.Equal("GitTooOld", error.Message);
    }
}
