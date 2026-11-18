namespace Trag.Infrastructure.Git.Parsing;

// What Trag looks for in Git's records: a gitlink row of ls-files --stage or ls-tree (a submodule or an embedded
// repository), and check-attr filter answers (path, attribute, value triplets) naming Git LFS.
internal static class GitEntries
{
    public static bool IsGitlink(ReadOnlySpan<byte> row) => row.StartsWith("160000 "u8);

    public static bool IsLfs(ReadOnlySpan<byte> filter) => filter.SequenceEqual("lfs"u8);

    public static bool HaveLfsFilter(IReadOnlyList<byte[]> checkAttrFields) =>
        checkAttrFields.Where((_, i) => i % 3 == 2).Any(field => IsLfs(field));
}
