namespace Trag.Domain.Naming;

// What a tag name has to be: the rules check-ref-format applies, said the way the create dialog can show them. Whether
// the name is taken is the writer's question, since the dialog has no list of tags; Git validates again before anything
// changes.
public static class TagNames
{
    public static string Error(string raw) => raw.Trim() is { Length: > 0 } name ? RefNames.Error(name, "tag") : "";

    public static bool IsValid(string raw) => raw.Trim().Length != 0 && Error(raw).Length == 0;
}
