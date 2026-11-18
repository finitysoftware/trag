using Trag.Domain.Naming;
using Trag.Domain.Writes;

namespace Trag.Domain.UnitTests.Naming;

// The people offered as co-authors: the repository's authors, and whoever the user types the way Git writes an identity.
public sealed class CoAuthorChoicesTests
{
    private static GitIdentity Person(string name, string email) => new(name, email);

    // The log lists authors newest first: each person comes once by email, the one with most commits first, a tie going
    // to the one seen most recently, under the name they last used; the user is not their own co-author.
    [Fact]
    public void WhenAuthorsAreRanked_ShouldListEachOnceMostFrequentFirstWithoutTheUser()
    {
        var me = Person("Me", "me@example.invalid");
        var authors = new[]
        {
            Person("Iva", "iva@example.invalid"), Person("Ana A.", "ana@example.invalid"), Person("Marko", "marko@example.invalid"), Person("Me", "ME@example.invalid"),
            Person("Ana", "ANA@example.invalid"), Person("Marko M.", "marko@example.invalid"),
            Person("Me", "me@example.invalid"), Person("Me", "me@example.invalid")
        };

        Assert.Equal([Person("Ana A.", "ana@example.invalid"), Person("Marko", "marko@example.invalid"), Person("Iva", "iva@example.invalid")],
            CoAuthorChoices.Rank(authors, me));
        Assert.Equal(Person("Me", "ME@example.invalid"), CoAuthorChoices.Rank(authors, null)[0]);
    }

    // An author Git recorded without an email (or with one Git cannot write back) would be a Co-authored-by line that never
    // reads back as a co-author, so the list cannot show or take it out again: such an author is not offered.
    [Fact]
    public void WhenAnAuthorHasNoUsableEmail_ShouldNotOfferThem()
    {
        GitIdentity[] authors = [Person("Nobody", ""), Person("Old", "old"), Person("Ana", "ana@example.invalid")];

        Assert.Equal([Person("Ana", "ana@example.invalid")], CoAuthorChoices.Rank(authors, null));
    }

    // A search finds a person by any part of their name or email; a typed "Name <email>" finds the person with that email,
    // or is offered first as the person to add.
    [Fact]
    public void WhenThePeopleAreSearched_ShouldMatchNameOrEmailAndOfferATypedIdentity()
    {
        GitIdentity[] people = [Person("Ana Anić", "ana@example.invalid"), Person("Marko M.", "marko@work.invalid")];

        Assert.Equal(people, CoAuthorChoices.Find(people, " "));
        Assert.Equal([people[0]], CoAuthorChoices.Find(people, "anić "));
        Assert.Equal([people[1]], CoAuthorChoices.Find(people, "WORK"));
        Assert.Equal([people[0]], CoAuthorChoices.Find(people, "Ana <ANA@example.invalid>"));
        Assert.Equal([Person("Iva", "iva@example.invalid")], CoAuthorChoices.Find(people, "Iva <iva@example.invalid>"));
        Assert.Empty(CoAuthorChoices.Find(people, "nobody"));
    }
}
