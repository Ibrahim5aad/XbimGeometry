using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Xbim.Common;

namespace Xbim.Geometry.Engine.Interop.Tests.Helpers;

/// <summary>
/// Minimal IItemSet implementation for mock IFC entities.
/// Copied from the main test project's MoqCreators.
/// </summary>
public class ItemListMoq<T> : IItemSet<T>
{
    private readonly List<T> _impl = new();

    public T this[int index] => _impl[index];
    T IList<T>.this[int index] { get => _impl[index]; set => _impl[index] = value; }

    public int Count => _impl.Count;
    public bool IsReadOnly => false;
    public IPersistEntity? OwningEntity => null;

#pragma warning disable CS0067 // Events required by interface but not raised
    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

    public void Add(T item) => _impl.Add(item);
    public void AddRange(IEnumerable<T> values) => _impl.AddRange(values);
    public void Clear() => _impl.Clear();
    public bool Contains(T item) => _impl.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => _impl.CopyTo(array, arrayIndex);

    public T FirstOrDefault(Func<T, bool> predicate) => _impl.FirstOrDefault()!;

    public TF FirstOrDefault<TF>(Func<TF, bool> predicate) where TF : T
    {
        var e = (TF?)_impl.FirstOrDefault();
        if (e != null && predicate(e)) return e;
        return default!;
    }

    public T GetAt(int index) => _impl[index];
    public IEnumerator<T> GetEnumerator() => _impl.GetEnumerator();
    public int IndexOf(T item) => _impl.IndexOf(item);
    public void Insert(int index, T item) => _impl.Insert(index, item);
    public bool Remove(T item) => _impl.Remove(item);
    public void RemoveAt(int index) => _impl.RemoveAt(index);

    public IEnumerable<TW> Where<TW>(Func<TW, bool> predicate) where TW : T
    {
        foreach (var item in _impl.Cast<TW>())
            if (predicate(item)) yield return item;
    }

    IEnumerator IEnumerable.GetEnumerator() => _impl.GetEnumerator();
}
