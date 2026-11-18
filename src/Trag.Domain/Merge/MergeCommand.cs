using Trag.Domain.Status;

namespace Trag.Domain.Merge;

// One commit merged into the branch the snapshot shows, with its message, the way its kind runs it: a pull merges what it
// fetched, a branch merge what the user chose, with the user's fast-forward choice.
public sealed record MergeCommand(RepositorySnapshot Before, string Target, MergeKind Kind, string Message, bool NoFastForward = false)
{
    public IProgress<string>? Progress { get; init; }
}
