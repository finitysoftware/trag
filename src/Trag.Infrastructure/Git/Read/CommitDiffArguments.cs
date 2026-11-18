using Trag.Domain.History;

namespace Trag.Infrastructure.Git.Read;

// What a commit is compared against: an empty tree for a root commit, the first parent for everything else. The file
// list and a single file's diff have to ask the same question, or the diff would not be of the file that was listed. A
// submodule the commit moved is listed whatever submodule.<name>.ignore says, as the status lists it.
internal static class CommitDiffArguments
{
    public static string[] For(CommitDetails commit, string[] format, string[]? paths = null) =>
        ["diff-tree", "-r", "--no-commit-id", "--no-ext-diff", "--no-textconv", "--no-color", "--ignore-submodules=none", .. format,
            .. commit.Parents.Count == 0 ? new[] { "--root", commit.Commit.Id } : new[] { commit.Parents[0], commit.Commit.Id }, "--", .. paths ?? []];
}
