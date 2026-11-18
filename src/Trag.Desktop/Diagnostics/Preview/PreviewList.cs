using System.Collections;

namespace Trag.Desktop.Diagnostics.Preview;

// Avalonia reads only visible indices; the fixture never constructs 50k view models.
public sealed class PreviewList(int count, Func<int, PreviewRow> create) : IList
{
    public int Count { get; } = count;
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public object this[int index]
    {
        get => index >= 0 && index < Count ? create(index) : throw new ArgumentOutOfRangeException(nameof(index));
        set => throw new NotSupportedException();
    }
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public bool Contains(object? value) => value is PreviewRow row && row.Number >= 1 && row.Number <= Count;
    public int IndexOf(object? value) => Contains(value) ? ((PreviewRow)value!).Number - 1 : -1;
    public IEnumerator GetEnumerator()
    {
        for (var index = 0; index < Count; index++) yield return this[index];
    }
    public void CopyTo(Array array, int index)
    {
        for (var i = 0; i < Count; i++) array.SetValue(this[i], index + i);
    }
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
