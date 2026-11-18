using System.Collections;
using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Application.Status;

// The files of one side of the status, the index or the working tree, in Domain's order. A refresh edits them in place with
// StatusDelta edits, never by a reset: Edited reports each edit with the file it replaced or removed, and a batch of
// edits comes between BatchStarting and BatchCompleted.
public sealed class StatusFiles(IReadOnlyList<FileStatus> files, bool fromIndex) : IReadOnlyList<FileStatus>
{
    private readonly List<FileStatus> files = StatusDelta.Ordered(files, fromIndex);
    public bool FromIndex { get; } = fromIndex;
    public int Count => files.Count;
    public FileStatus this[int index] => files[index];
    public event Action? BatchStarting;
    public event Action? BatchCompleted;
    public event Action<StatusDelta.Edit, FileStatus?>? Edited;

    public FileStatus[] Snapshot() => files.ToArray();
    public int IndexOf(GitPath path) => files.FindIndex(file => file.Path.IsSame(path));

    public void ApplyBatch(IReadOnlyList<StatusDelta.Edit> edits)
    {
        BatchStarting?.Invoke();
        try
        {
            foreach (var edit in edits) Apply(edit);
        }
        finally { BatchCompleted?.Invoke(); }
    }

    private void Apply(StatusDelta.Edit edit)
    {
        FileStatus? old = null;
        if (edit.File is null)
        {
            old = files[edit.Index];
            files.RemoveAt(edit.Index);
        }
        else if (edit.Replace)
        {
            old = files[edit.Index];
            files[edit.Index] = edit.File;
        }
        else files.Insert(edit.Index, edit.File);
        Edited?.Invoke(edit, old);
    }

    public IEnumerator<FileStatus> GetEnumerator() => files.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
