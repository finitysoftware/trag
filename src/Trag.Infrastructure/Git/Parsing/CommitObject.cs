namespace Trag.Infrastructure.Git.Parsing;

// A commit as git cat-file commit prints it: its tree on the first line, then a line for each parent. It still names the
// parent of the oldest commit of a shallow clone, which every other read hides.
internal static class CommitObject
{
    public static bool NamesParent(byte[] commit) => GitBytes.Text(commit).Split('\n', 3)[1].StartsWith("parent ", StringComparison.Ordinal);
}
