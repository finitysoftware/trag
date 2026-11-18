namespace Trag.Domain.Naming;

// What a branch name has to be: the rules check-ref-format applies, said the way a dialog can show them, plus the ones
// Git cannot know from the name alone. Git keeps a branch as a file whose name ignores case on Windows, so the name is
// not taken locally, whatever its case, and it is neither the folder of another branch nor inside one's: topic and
// topic/x cannot both be branches. Git validates again before anything changes. Also suggests a name from a commit
// subject: up to six lower-case ASCII words joined by '-', otherwise the short id.
public static class BranchNames
{
    public static string Error(string raw, IEnumerable<string> existing) => Error(raw, existing, null);

    // A rename is judged the same way, except that the branch's own old name never stands in the way of a name inside it
    // or holding it: Git moves the branch out of the way first.
    public static string RenameError(string raw, string renamed, IEnumerable<string> existing) => Error(raw, existing, renamed);

    public static bool IsValid(string raw, IEnumerable<string> existing) => raw.Trim().Length != 0 && Error(raw, existing).Length == 0;

    public static bool IsValidRename(string raw, string renamed, IEnumerable<string> existing) =>
        raw.Trim().Length != 0 && RenameError(raw, renamed, existing).Length == 0;

    private static string Error(string raw, IEnumerable<string> existing, string? renamed)
    {
        var name = raw.Trim();
        if (name.Length == 0)
            return "";
        if (RefNames.Error(name, "branch") is { Length: > 0 } error)
            return error;
        var others = existing.ToArray();
        if (others.Contains(name, StringComparer.OrdinalIgnoreCase))
            return "A branch with this name already exists.";
        if (others.FirstOrDefault(other => other != renamed && Nests(name, other)) is { } inTheWay)
            return "A branch cannot be named " + name + " while " + inTheWay + " exists.";
        return "";
    }

    private static bool Nests(string name, string other) =>
        name.StartsWith(other + "/", StringComparison.OrdinalIgnoreCase) || other.StartsWith(name + "/", StringComparison.OrdinalIgnoreCase);

    // The suggestion itself, or the first "suggestion-n" that is not taken.
    public static string UniqueName(string suggestion, IEnumerable<string> existing)
    {
        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = suggestion;
        for (var n = 2; taken.Contains(name); n++)
            name = suggestion + "-" + n;
        return name;
    }

    // A branch for a commit: named after its subject, or its short id, and free among the existing ones.
    public static string SuggestFor(string subject, string commitId, IEnumerable<string> existing) =>
        UniqueName(Suggest(subject, GitObjectId.Short(commitId)), existing);

    // A branch that keeps the commits a detached HEAD would leave behind.
    public static string SuggestKept(string head, IEnumerable<string> existing) =>
        UniqueName("kept-from-" + GitObjectId.Short(head), existing);

    public static string Suggest(string subject, string shortId)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var raw in subject.Normalize(System.Text.NormalizationForm.FormD))
        {
            var ch = raw is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(raw);
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(ch)) { current.Append(ch); continue; }
            if (current.Length == 0) continue;
            words.Add(current.ToString()); current.Clear();
            if (words.Count == 6) break;
        }
        if (current.Length != 0 && words.Count < 6) words.Add(current.ToString());
        var name = string.Join('-', words);
        return name.Length == 0 ? "from-" + shortId : name;
    }
}
