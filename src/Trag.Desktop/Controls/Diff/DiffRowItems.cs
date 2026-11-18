using System.Collections;

namespace Trag.Desktop.Controls.Diff;

internal sealed class DiffRowItems(object identity, int count, bool split) : IList
{
    public int Count => count;
    public object? this[int index] { get => new DiffRowData(identity, index, split); set => throw new NotSupportedException(); }
    public int IndexOf(object? value) => value is DiffRowData row && ReferenceEquals(row.Identity, identity) && row.Split == split ? row.Index : -1;
    public bool Contains(object? value) => IndexOf(value) >= 0;
    public IEnumerator GetEnumerator() { for (var i = 0; i < Count; i++) yield return this[i]; }
    public void CopyTo(Array array, int index) { for (var i = 0; i < Count; i++) array.SetValue(this[i], index + i); }
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
