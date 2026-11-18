namespace Trag.Domain.Naming;

// The questions about a ref name that are answered before Git is asked: it is present, short enough, has no control
// or white-space character, and does not start with '-', which Git would read as an option; and, for a branch or a tag a
// dialog names, which of check-ref-format's rules it breaks, or of the two Windows adds for a ref it keeps as a file.
// check-ref-format decides the rest; what a rejected name means to the user otherwise stays with the caller.
public static class RefNames
{
    // What check-ref-format refuses anywhere in a name, and at its end.
    private static readonly char[] ForbiddenCharacters = ['~', '^', ':', '?', '*', '[', '\\'];
    private static readonly string[] InvalidSequences = ["..", "//", "@{"];
    private static bool EndsLikeALock(string name) => name.EndsWith(".lock", StringComparison.Ordinal);
    // A file cannot be named after a device, whatever its case or extension; Git accepts such a ref and cannot write it.
    private static readonly string[] Devices = ["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        .. "0123456789".SelectMany(digit => new[] { "COM" + digit, "LPT" + digit })];
    private static bool IsDevice(string part) => Devices.Contains(part.Split('.')[0], StringComparer.OrdinalIgnoreCase);

    // The rules check-ref-format applies to a branch or a tag name, said the way a dialog can show them for that kind of
    // name ("branch", "tag"); empty when the trimmed, non-empty name breaks none.
    public static string Error(string name, string kind)
    {
        var plural = char.ToUpperInvariant(kind[0]) + kind[1..] + " names";
        if (name.Any(char.IsWhiteSpace) || name.Any(char.IsControl))
            return plural + " cannot contain spaces.";
        if (name.Length > GitLimits.NameLength)
            return plural + " can have at most " + GitLimits.NameLength + " characters.";
        if (name[0] is '-' or '/' || name[^1] is '/' or '.' || EndsLikeALock(name))
            return "A " + kind + " name cannot start with '-' or '/', or end with '/', '.' or '.lock'.";
        if (InvalidSequences.Any(sequence => name.Contains(sequence, StringComparison.Ordinal)) || name.IndexOfAny(ForbiddenCharacters) >= 0)
            return plural + " cannot contain '..', '~', '^', ':', '?', '*', '[', '\\' or '@{'.";
        var parts = name.Split('/');
        if (parts.Any(part => part.StartsWith('.') || EndsLikeALock(part)))
            return "Parts of a " + kind + " name cannot start with '.' or end with '.lock'.";
        if (parts.Any(part => part.EndsWith('.')))
            return "Parts of a " + kind + " name cannot end with '.' on Windows.";
        if (parts.Any(IsDevice))
            return "Parts of a " + kind + " name cannot be a name Windows keeps for a device, such as CON, NUL or COM1.";
        return "";
    }

    public static bool HasSafeCharacters(string value, int maxLength) =>
        value.Length is not 0 && value.Length <= maxLength && !value.StartsWith('-')
        && !value.Any(char.IsControl) && !value.Any(char.IsWhiteSpace);

    // A remote's branch as this repository tracks it, as "refs/remotes/origin/main".
    public static bool IsRemoteTracking(string fullName) => fullName.StartsWith("refs/remotes/", StringComparison.Ordinal);
    // An upstream as a person names it: "refs/remotes/origin/main" is "origin/main"; anything else stays as Git wrote it.
    public static string RemoteTrackingName(string upstream) => IsRemoteTracking(upstream) ? upstream["refs/remotes/".Length..] : upstream;

    // The branch a full ref name is, "refs/heads/main" being "main"; any other ref, or "HEAD" for a detached HEAD, is none.
    // Git's own shortening would write heads/main when a tag is named main too.
    public static string? LocalBranch(string fullName) =>
        fullName.StartsWith("refs/heads/", StringComparison.Ordinal) ? fullName["refs/heads/".Length..] : null;
}
