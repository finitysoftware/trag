using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

// git shortlog --summary --numbered --email as Git 2.55 prints it for the repository details: the count right-aligned,
// a tab, then the name and the address in angle brackets.
public sealed class ShortlogParserTests
{
    private static (string Name, string Email, int Commits)[] Parse(string text) =>
        [.. ShortlogParser.Parse(text).Select(contributor => (contributor.Identity.Name, contributor.Identity.Email, contributor.Commits))];

    [Fact]
    public void WhenContributorsAreListed_ShouldReadEachCountNameAndAddressInOrder() =>
        Assert.Equal([("Bo [ops]", "bo@example.com", 2), ("Ana", "ana@example.com", 1)],
            Parse("     2\tBo [ops] <bo@example.com>\n     1\tAna <ana@example.com>\n"));

    // Git refuses an empty name, but a commit another tool wrote can have one, and Git lists its address alone.
    [Fact]
    public void WhenAnAuthorHasNoName_ShouldReadTheAddressWithAnEmptyName() =>
        Assert.Equal([("", "nameless@example.com", 1)], Parse("     1\t <nameless@example.com>\n"));
}
