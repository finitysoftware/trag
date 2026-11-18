namespace Trag.Domain.Presentation.Text;

// What the file status says of submodules not at the commits the branch records, and how their update ended.
public static class SubmoduleText
{
    public static string NotAtRecorded(IReadOnlyList<string> paths) => paths.Count == 1
        ? "Submodule " + paths[0] + " is not at the commit this branch records. Update checks that commit out; staging " + paths[0] + " keeps its new commit instead."
        : Counted.Of(paths.Count, "submodule") + " are not at the commits this branch records: " + string.Join(", ", paths)
            + ". Update checks those commits out; staging one keeps its new commit instead.";

    public const string Updating = "Updating submodules…";
    public const string Updated = "Submodules are at the commits this branch records.";
    public const string NotUpdated = "Git could not update every submodule. Review the details below.";
    public static readonly string Changed = SyncOutcomeText.StateUnsupported("updating submodules");
}
