namespace Trag.Infrastructure.Git.Parsing;

// One row of for-each-ref --format=%(refname)%00%(objectname)%00%(upstream)%00%(symref)%00%(HEAD), where HEAD is "*" on
// the checked-out branch. What the fields mean, and in which order they are judged, stays with the reader.
internal static class BranchRefParser
{
    public static (string Name, string Id, string Upstream, string Symref, bool IsHead) Parse(string row)
    {
        var fields = row.TrimEnd('\r').Split('\0');
        if (fields.Length != 5) throw new FormatException("Invalid branch record.");
        return (fields[0], fields[1], fields[2], fields[3], fields[4] == "*");
    }
}
