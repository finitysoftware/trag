namespace Trag.Domain.Sync;

// One configured fetch refspec, read in both directions: which local branch a remote-tracking ref came from, and where a
// local branch would be fetched to. A pattern maps through its single star; a plain refspec maps only itself.
public sealed record RemoteRefspecMapping(string Source, string Destination)
{
    public static RemoteRefspecMapping? Parse(string refspec, string remote)
    {
        var parts = refspec.TrimStart('+').Split(':');
        if (parts.Length != 2) return null;
        if (!parts[0].StartsWith("refs/heads/", StringComparison.Ordinal)) return null;
        if (!parts[1].StartsWith("refs/remotes/" + remote + "/", StringComparison.Ordinal)) return null;
        if (parts[0].Count(c => c == '*') != parts[1].Count(c => c == '*')) return null;
        return new(parts[0], parts[1]);
    }

    public string? SourceFor(string destination) => Map(destination, Destination, Source);

    public string? DestinationFor(string source) => Map(source, Source, Destination);

    private static string? Map(string value, string pattern, string replacement)
    {
        var star = pattern.IndexOf('*');
        if (star < 0) return value == pattern ? replacement : null;
        return value.StartsWith(pattern[..star], StringComparison.Ordinal) && value.EndsWith(pattern[(star + 1)..], StringComparison.Ordinal)
            && value.Length >= pattern.Length - 1 ? replacement.Replace("*", value[star..(value.Length - pattern.Length + star + 1)], StringComparison.Ordinal) : null;
    }
}
