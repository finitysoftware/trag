namespace Trag.Infrastructure.Git.Processes;

// Paths given to Git on its command line, as many per call as the line holds: their characters, each quoted and spaced,
// stay well under the 32,767 Windows allows for the whole line. A single longer name still goes alone.
internal static class PathBatches
{
    private const int Budget = 24_000;

    public static IEnumerable<string[]> Of(IEnumerable<string> paths)
    {
        var batch = new List<string>();
        var length = 0;
        foreach (var path in paths)
        {
            if (batch.Count != 0 && length + path.Length + 3 > Budget) { yield return [.. batch]; batch.Clear(); length = 0; }
            batch.Add(path);
            length += path.Length + 3;
        }
        if (batch.Count != 0) yield return [.. batch];
    }
}
