using Trag.Domain.Writes;

namespace Trag.Domain.Naming;

// The people offered as co-authors: the repository's authors, and whoever the user types the way Git writes an identity.
public static class CoAuthorChoices
{
    // Authors as the log lists them, newest first: each person once by email under the name they last used, the one with
    // most commits first and a tie going to the one seen most recently; the user is not their own co-author, and an author
    // whose identity would not read back from a Co-authored-by line is not offered.
    public static IReadOnlyList<GitIdentity> Rank(IReadOnlyList<GitIdentity> authors, GitIdentity? self) =>
    [
        .. authors.Where(author => GitIdentity.Parse(author.Display) is not null)
            .GroupBy(author => author.Email, StringComparer.OrdinalIgnoreCase)
            .Where(group => self is null || !self.IsSamePerson(group.First()))
            .OrderByDescending(group => group.Count())
            .Select(group => group.First())
    ];

    // A search matches any part of a name or an email; a typed "Name <email>" finds the person with that email, or is the
    // person to add.
    public static IReadOnlyList<GitIdentity> Find(IReadOnlyList<GitIdentity> people, string text)
    {
        var search = text.Trim();
        if (GitIdentity.Parse(text) is not { } typed)
            return [.. people.Where(person => person.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || person.Email.Contains(search, StringComparison.OrdinalIgnoreCase))];
        var known = people.Where(typed.IsSamePerson).ToArray();
        return known.Length == 0 ? [typed] : known;
    }
}
