namespace Trag.Domain.Paths;

// How this machine's file system tells two paths apart: without regard to case on Windows, exactly everywhere else. A
// folder's key is its full path without a trailing separator; a path that cannot be made full is keyed as it was given.
public static class LocalPaths
{
    public static readonly StringComparison Comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static readonly StringComparer Comparer = StringComparer.FromComparison(Comparison);

    public static bool Same(string first, string second) => Comparer.Equals(Key(first), Key(second));

    public static string Key(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException) { return path.Trim(); }
    }
}
