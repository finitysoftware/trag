namespace Trag.Infrastructure.Git.Parsing;

// One row of for-each-ref --format=%(refname)%00%(objectname)%00%(symref) over a remote's refs.
internal static class RemoteRefParser
{
    public static (string RefName, string ObjectName, bool IsSymref) Parse(string row)
    {
        var fields = row.TrimEnd('\r').Split('\0');
        if (fields.Length != 3) throw new FormatException("Invalid remote ref.");
        return (fields[0], fields[1], fields[2].Length != 0);
    }
}
