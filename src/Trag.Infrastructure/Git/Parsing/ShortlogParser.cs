using System.Globalization;
using Trag.Domain.Details;

namespace Trag.Infrastructure.Git.Parsing;

// git shortlog --summary --numbered --email: one line per person, the commit count right-aligned, a tab, then the name
// and the address in angle brackets. The address is the last bracketed part, so a name may hold brackets of its own.
internal static class ShortlogParser
{
    public static IReadOnlyList<Contributor> Parse(string text) =>
        [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).Select(Person)];

    private static Contributor Person(string line)
    {
        var tab = line.IndexOf('\t');
        var who = line[(tab + 1)..];
        var address = who.LastIndexOf(" <", StringComparison.Ordinal);
        if (tab < 0 || address < 0 || !who.EndsWith('>')) throw new FormatException("Invalid shortlog line.");
        return new(new(who[..address], who[(address + 2)..^1]), int.Parse(line[..tab], NumberStyles.AllowLeadingWhite, CultureInfo.InvariantCulture));
    }
}
