using System.Text;
using Trag.Domain.History;

namespace Trag.Infrastructure.Git.Parsing;

// git log --decorate=full, %D: the names on one commit, comma separated. "HEAD -> refs/heads/main" is the checked-out
// branch, "HEAD" alone a detached HEAD, "tag: refs/tags/v1" a tag, and every other name is a full ref. A ref name holds
// no space, so ", " never splits one. Only branches, remote-tracking branches and tags are kept; a remote's own HEAD and
// Git's private refs (stash, notes, bisect) are not. The names come HEAD's own first, then local branches, remote ones and
// tags, each in Git's order; a name is clipped like any display text, and a commit keeps at most a few dozen.
internal static class CommitRefParser
{
    private const int MaxRefs = 32;
    private const string Head = "HEAD -> ";
    private const string Tag = "tag: ";

    public static IReadOnlyList<CommitRef> Parse(ReadOnlySpan<byte> decorations)
    {
        if (decorations.IsEmpty) return [];
        List<CommitRef> refs = [];
        foreach (var part in Encoding.UTF8.GetString(decorations).Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            if (refs.Count == MaxRefs) break;
            if (Read(part) is { } name) refs.Add(name);
        }
        return [.. refs.OrderBy(name => name.Kind)];
    }

    private static CommitRef? Read(string part)
    {
        if (part == "HEAD") return new("HEAD", CommitRefKind.DetachedHead);
        if (part.StartsWith(Head, StringComparison.Ordinal)) return Named(part[Head.Length..], "refs/heads/", CommitRefKind.CurrentBranch);
        if (part.StartsWith(Tag, StringComparison.Ordinal)) return Named(part[Tag.Length..], "refs/tags/", CommitRefKind.Tag);
        if (part.StartsWith("refs/remotes/", StringComparison.Ordinal))
            return part.EndsWith("/HEAD", StringComparison.Ordinal) ? null : Named(part, "refs/remotes/", CommitRefKind.RemoteBranch);
        return Named(part, "refs/heads/", CommitRefKind.Branch);
    }

    private static CommitRef? Named(string full, string prefix, CommitRefKind kind) =>
        full.Length > prefix.Length && full.StartsWith(prefix, StringComparison.Ordinal)
            ? new(DisplayText.Clip(Encoding.UTF8.GetBytes(full[prefix.Length..]), 256), kind) : null;
}
