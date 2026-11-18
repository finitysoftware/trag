namespace Trag.TestSupport;

// The one way a throwaway fixture folder is removed. Git, its hooks and their shells may still be exiting when a test
// ends, so a folder that is briefly busy is retried for a few seconds; one that stays busy fails with the path that
// was held and the attempts made, so a cleanup failure reads as one. A path outside the temp folder or without the
// fixture prefix is never touched.
public static class FixtureDirectory
{
    private const int Attempts = 20;
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(250);

    public static void Delete(string root, string prefix)
    {
        var full = Path.GetFullPath(root);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(full).StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Unsafe fixture path.");
        if (!Directory.Exists(full)) return;
        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(full, recursive: true);
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (attempt == Attempts)
                    throw new IOException("Fixture cleanup could not remove " + full + " after " + attempt + " attempts: " + error.Message, error);
                Thread.Sleep(Pause);
            }
        }
    }
}
