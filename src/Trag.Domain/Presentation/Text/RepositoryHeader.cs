using Trag.Domain.Catalog;
using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// The header texts over a snapshot: name, branch, Git version, the warnings, and for a submodule the way back to the
// repository it belongs to. The shell keeps the current one as it follows the repository.
public sealed record RepositoryHeader
{
    public static readonly RepositoryHeader Empty = new();

    public string RepositoryName { get; init; } = "No repository open";
    public string BranchText { get; init; } = "—";
    public string GitVersionText { get; init; } = "";
    public string Warning { get; init; } = "";
    public string Superproject { get; init; } = "";

    public static RepositoryHeader From(RepositorySnapshot snapshot)
    {
        // While a rebase replays commits Git detaches HEAD; the branch is still the one being rebased.
        var branch = snapshot.Branch ?? (snapshot.Rebase is { Branch.Length: > 0 } rebase ? RebaseText.Rebasing(rebase) : DetachedBranch(snapshot.Head));
        if (snapshot.IsUnborn)
            branch += " · no commits yet";

        return new()
        {
            RepositoryName = RepositoryEntry.DisplayName(snapshot.Root),
            BranchText = branch,
            GitVersionText = "Git " + snapshot.GitVersion,
            Warning = Warnings(snapshot),
            Superproject = snapshot.Superproject is { } superproject ? "Back to " + RepositoryEntry.DisplayName(superproject) : ""
        };
    }

    private static string DetachedBranch(string? head) => head is null ? "Detached HEAD" : "Detached at " + GitObjectId.Short(head);

    private static string Warnings(RepositorySnapshot snapshot)
    {
        var warnings = snapshot.Limitations.ToList();
        if (snapshot.IsDetachedAtCommit && snapshot.Head is { } head)
            warnings.Insert(0, new DetachedHead(head, snapshot.DetachedCommits).Warning);
        if (snapshot.Operation != RepositoryOperation.None && !snapshot.HasSupportedResolution)
            warnings.Insert(0, snapshot.Rebase is { Limitation.Length: > 0 } rebase ? rebase.Limitation
                : $"{OperationName(snapshot.Operation)} in progress. Resolve or finish it in your Git tools, then refresh.");
        return string.Join("\n", warnings);
    }

    public static string OperationName(RepositoryOperation operation) => operation switch
    {
        RepositoryOperation.CherryPick => "Cherry-pick",
        RepositoryOperation.ApplyMailbox => "Patch application",
        RepositoryOperation.Sequencer => "Git sequence",
        RepositoryOperation.Conflict => MergeText.ConflictResolution,
        _ => operation.ToString()
    };
}
