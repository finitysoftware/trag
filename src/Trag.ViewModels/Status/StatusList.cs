using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Trag.Application.Status;
using Trag.Domain.Status;

namespace Trag.ViewModels.Status;

// The list a file pane binds to: the files of one side (Domain's StatusFiles) as rows, materialized only for the indices the
// view asks for. Their edits arrive as collection changes, never as a reset.
public sealed class StatusList : IList, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly StatusFiles files;

    public StatusList(StatusFiles files)
    {
        this.files = files;
        files.BatchStarting += () => BatchStarting?.Invoke(this, EventArgs.Empty);
        files.BatchCompleted += () => BatchCompleted?.Invoke(this, EventArgs.Empty);
        files.Edited += FollowEdit;
    }


    public int Count => files.Count;
    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? BatchStarting;
    public event EventHandler? BatchCompleted;

    private void FollowEdit(StatusDelta.Edit edit, FileStatus? old)
    {
        NotifyCollectionChangedEventArgs change;
        if (edit.File is null) change = new(NotifyCollectionChangedAction.Remove, Row(old!), edit.Index);
        else if (edit.Replace) change = new(NotifyCollectionChangedAction.Replace, this[edit.Index], Row(old!), edit.Index);
        else change = new(NotifyCollectionChangedAction.Add, this[edit.Index], edit.Index);
        CollectionChanged?.Invoke(this, change);
        if (!edit.Replace)
            PropertyChanged?.Invoke(this, new(nameof(Count)));
    }

    private StatusRow Row(FileStatus file) => new(file, files.FromIndex);

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public object this[int index] { get => Row(files[index]); set => throw new NotSupportedException(); }
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public bool Contains(object? value) => IndexOf(value) >= 0;
    public int IndexOf(object? value) => value is StatusRow row && row.FromIndex == files.FromIndex ? files.IndexOf(row.File.Path) : -1;
    public IEnumerator GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
            yield return this[i];
    }
    public void CopyTo(Array array, int index)
    {
        for (var i = 0; i < Count; i++)
            array.SetValue(this[i], index + i);
    }
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
