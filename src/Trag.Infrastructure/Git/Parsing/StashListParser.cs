using Trag.Domain;
using Trag.Domain.Stash;

namespace Trag.Infrastructure.Git.Parsing;

internal static class StashListParser
{
    // The author date as seconds since the epoch: Git takes any zone offset from GIT_AUTHOR_DATE, also one beyond the
    // fourteen hours an ISO date is read with.
    public const string Format = "--format=%H%x00%gs%x00%at";

    // List order is stash@{0} first; the index is the row number.
    public static IReadOnlyList<StashEntry> Parse(string output)
    {
        var entries = new List<StashEntry>();
        foreach (var row in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = row.TrimEnd('\r').Split('\0');
            if (fields.Length != 3) throw new FormatException("Invalid stash record.");
            var (id, message, date) = (fields[0], fields[1], fields[2]);
            if (!GitObjectId.IsValid(id)) throw new FormatException("Invalid stash object ID.");
            // Git takes a date past the year 9999, past even a long, and %at prints it as given; Git's %aI shows it as 1970.
            var when = long.TryParse(date, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds() ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.UnixEpoch;
            entries.Add(new(id, entries.Count, message, StashEntry.BranchOf(message), when));
        }
        return entries;
    }
}
