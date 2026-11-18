namespace Trag.Domain.Naming;

// What a remote name has to be before Git is asked to add it, by Git's own ref name rules and said the way a dialog can
// show it, plus the one thing Git cannot know from the name alone: it is not taken in this repository, whatever its
// case, since Windows keeps a remote's remote-tracking branches in a folder whose name ignores case, nor does it share
// those branches with a taken one, which Git up to 2.50 allows.
public static class RemoteNames
{
    public static string Error(string name, IEnumerable<string> existing)
    {
        if (name.Length == 0) return "Enter a remote name such as origin.";
        if (RefNames.Error(name, "remote") is { Length: > 0 } error) return error;
        if (Taken(name, existing) is { } taken)
            return taken.Equals(name, StringComparison.OrdinalIgnoreCase) ? "A remote named " + taken + " already exists. Choose another name."
                : "Remote " + taken + " would share its remote-tracking branches with this name. Choose another name.";
        return "";
    }

    // The remote a new one under this name would collide with; null while the name is free.
    public static string? Taken(string name, IEnumerable<string> existing) => existing.FirstOrDefault(other => Overlap(other, name));

    // Two remotes share remote-tracking branches when their names are the same, or one is the other followed by a slash
    // and more, whatever the case: refs/remotes/<name>/ holds both, in one folder on Windows.
    public static bool Overlap(string one, string other) => (one + "/").StartsWith(other + "/", StringComparison.OrdinalIgnoreCase)
        || (other + "/").StartsWith(one + "/", StringComparison.OrdinalIgnoreCase);

    public static bool IsValid(string name) => Error(name, []).Length == 0;
}
