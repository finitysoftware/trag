using Trag.Domain.Paths;

namespace Trag.Domain.Status;

// The rows of one side of the status pane (conflicts have a group of their own), and the edits that turn one reading into
// the next. Rows are ordered by their path bytes so that two readings can be walked together; the edits are planned from
// the end, so every index is still valid when they are applied in order: a removal, an insertion, or a replacement where
// the change itself differs.
public static class StatusDelta
{
    public sealed record Edit(int Index, FileStatus? File, bool Replace = false);

    public static List<FileStatus> Ordered(IEnumerable<FileStatus> files, bool fromIndex) =>
        files.Where(file => fromIndex ? file.IsStaged : file.IsWorkingTreeChange).OrderBy(file => file.Path, GitPath.ByBytes).ToList();

    public static IReadOnlyList<Edit> Plan(IReadOnlyList<FileStatus> before, IReadOnlyList<FileStatus> after)
    {
        var edits = new List<Edit>();
        var i = before.Count - 1;
        var j = after.Count - 1;
        while (i >= 0 || j >= 0)
        {
            var order = i < 0 ? -1 : j < 0 ? 1 : GitPath.ByBytes.Compare(before[i].Path, after[j].Path);
            if (order > 0)
                edits.Add(new(i--, null));
            else if (order < 0)
                edits.Add(new(i + 1, after[j--]));
            else
            {
                if (!before[i].HasSameChange(after[j]))
                    edits.Add(new(i, after[j], true));
                i--;
                j--;
            }
        }
        return edits;
    }
}
