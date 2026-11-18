namespace Trag.Domain.Writes;

public sealed record GitIdentity(string Name, string Email)
{
    // Git's own way of writing an identity, which is what a user expects to read next to a commit.
    public static string Format(string name, string email) => name + " <" + email + ">";
    public string Display => Format(Name, Email);
    // One person may write their email in any case.
    public bool IsSamePerson(GitIdentity other) => string.Equals(Email, other.Email, StringComparison.OrdinalIgnoreCase);

    // "Name <email>" as Git writes an identity: neither part holds a line break or an angle bracket, and the email has its @.
    public static GitIdentity? Parse(string text)
    {
        var trimmed = text.Trim();
        var open = trimmed.IndexOf('<');
        if (open < 0 || !trimmed.EndsWith('>') || text.IndexOfAny(['\r', '\n']) >= 0) return null;
        var name = trimmed[..open].Trim();
        var email = trimmed[(open + 1)..^1];
        return name.Length == 0 || !email.Contains('@') || (name + email).IndexOfAny(['<', '>']) >= 0 ? null : new(name, email);
    }
}
