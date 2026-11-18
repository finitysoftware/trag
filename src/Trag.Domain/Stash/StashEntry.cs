namespace Trag.Domain.Stash;

// One entry of git stash list. Index is the position at read time and shifts after a drop, so writes identify
// an entry by Id and resolve the index again right before the command.
public sealed record StashEntry(string Id, int Index, string Message, string? Branch, DateTimeOffset When)
{
    // The stash commit as the commit details pane reads it: its id, its message, and when it was made.
    public History.HistoryCommit AsCommit() => new(Id, "", "", When.ToString("O", System.Globalization.CultureInfo.InvariantCulture), Message);
    public string Reference => "stash@{" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
    // Git writes "WIP on main: 1a2b3c4 Subject" for the default and "On main: text" for a named stash.
    public static string? BranchOf(string message)
    {
        var text = message.StartsWith("WIP on ", StringComparison.Ordinal) ? message[7..] : message.StartsWith("On ", StringComparison.Ordinal) ? message[3..] : null;
        if (text is null) return null;
        var colon = text.IndexOf(':');
        return colon <= 0 ? null : text[..colon];
    }
    // The part after the branch: the user's own words, or Git's short id and subject.
    public string Title
    {
        get
        {
            var prefixed = Message.StartsWith("WIP on ", StringComparison.Ordinal) || Message.StartsWith("On ", StringComparison.Ordinal);
            var at = prefixed ? Message.IndexOf(": ", StringComparison.Ordinal) : -1;
            return at > 0 && at + 2 < Message.Length ? Message[(at + 2)..] : Message;
        }
    }
}
