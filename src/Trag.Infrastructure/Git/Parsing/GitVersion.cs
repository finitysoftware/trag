using System.Globalization;
using Trag.Domain.Errors;

namespace Trag.Infrastructure.Git.Parsing;

// "git version 2.55.0.windows.3" is a version, not a decision: whether that version is usable is GitVersionPolicy's.
// Git built from a source snapshot without tags says "2.56.GIT", read as patch 0 of the release in development.
internal static class GitVersion
{
    public static Version Parse(string output)
    {
        const string prefix = "git version ";
        if (!output.StartsWith(prefix, StringComparison.Ordinal)) throw new RepositoryException(RepositoryError.InvalidGitVersion);
        var parts = output[prefix.Length..].Split('.');
        if (parts.Length < 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(parts[2] is "GIT" ? "0" : new string(parts[2].TakeWhile(char.IsAsciiDigit).ToArray()), out var patch))
            throw new RepositoryException(RepositoryError.InvalidGitVersion);
        return new Version(major, minor, patch);
    }
}
