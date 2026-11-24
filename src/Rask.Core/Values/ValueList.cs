using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rask.Values;

/// <summary>
///     A list that is a value: two with the same elements in the same order are equal, and nothing changes one once it
///     is made — <c>public ValueList&lt;CategoryCode&gt; Codes { get; private set; } = [];</c>.
/// </summary>
/// <remarks>
///     <para>
///         Made from a collection expression (<c>[a, b]</c>) or <see cref="ValueList.Of{T}(IEnumerable{T})" />. A value object
///         that holds one — <c>record LocalWell(…, ValueList&lt;Pair&gt; Tanks)</c> — compares by what the list holds, as a
///         record holding a <c>List&lt;T&gt;</c> does not.
///     </para>
///     <para>
///         On an aggregate or an entity it is one JSON column (an EF Core complex collection) when it holds value objects,
///         and a primitive collection when it holds plain values — the same columns an <c>IReadOnlyList&lt;T&gt;</c> gets.
///         Inside a value object it travels in that value object's JSON column: map the holder with
///         <c>b.Json(t =&gt; t.Settings)</c>.
///     </para>
///     <para>
///         It implements <see cref="IList{T}" /> because that is the shape EF Core reads a JSON collection into — filling
///         the list before anybody reads it — and only for that: a list that has been read is frozen, and anything that
///         changes it afterwards throws <see cref="NotSupportedException" />. Make a new one instead.
///     </para>
/// </remarks>
/// <typeparam name="T">What it holds.</typeparam>
[CollectionBuilder(typeof(ValueList), nameof(ValueList.Of))]
#pragma warning disable CA1710 // a value, read as the domain says it ("a list of codes"), not a "Collection"
public sealed class ValueList<T> : IList<T>, IReadOnlyList<T>, IList, IEquatable<ValueList<T>>
#pragma warning restore CA1710
{
    private readonly List<T> _items;
    private bool _frozen;

    /// <summary>An empty list. For EF Core, which fills it as it reads a row; code writes <c>[]</c>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueList() => _items = [];

    internal ValueList(List<T> items) => _items = items;

    /// <summary>How many elements it holds.</summary>
    public int Count => Read().Count;

    /// <summary>The element at <paramref name="index" />.</summary>
    /// <param name="index">Its position, from 0.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is outside the list.</exception>
    public T this[int index] => Read()[index];

    // What a reader filling the list asks as it goes — EF Core checks it does not hold an element twice — reads without
    // freezing it: only what code reads through the list's own members does.
    int ICollection<T>.Count => _items.Count;

    int ICollection.Count => _items.Count;

    T IList<T>.this[int index]
    {
        get => _items[index];
        set => Building()[index] = value;
    }

    bool ICollection<T>.IsReadOnly => _frozen;

    bool IList.IsReadOnly => _frozen;

    bool IList.IsFixedSize => _frozen;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    object? IList.this[int index]
    {
        get => _items[index];
        set => ((IList)Building())[index] = value;
    }

    /// <summary>Whether it holds <paramref name="item" />.</summary>
    /// <param name="item">The element to look for.</param>
    /// <returns><see langword="true" /> when it is there.</returns>
    public bool Contains(T item) => Read().Contains(item);

    /// <summary>Where <paramref name="item" /> first is, or -1.</summary>
    /// <param name="item">The element to look for.</param>
    /// <returns>Its position, from 0.</returns>
    public int IndexOf(T item) => Read().IndexOf(item);

    /// <summary>Copies the elements into <paramref name="array" />.</summary>
    /// <param name="array">Where they go.</param>
    /// <param name="arrayIndex">The first position written.</param>
    public void CopyTo(T[] array, int arrayIndex) => Read().CopyTo(array, arrayIndex);

    /// <summary>Walks the elements in order.</summary>
    /// <returns>The enumerator.</returns>
    public List<T>.Enumerator GetEnumerator() => Read().GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Whether <paramref name="other" /> holds the same elements in the same order.</summary>
    /// <param name="other">The other list.</param>
    /// <returns><see langword="true" /> when they are the same value.</returns>
    public bool Equals(ValueList<T>? other) =>
        other is not null && (ReferenceEquals(this, other) || Read().SequenceEqual(other.Read()));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ValueList<T>);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var item in Read())
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    /// <summary>The elements, as a list reads: <c>[A, B]</c>.</summary>
    /// <returns>The elements between brackets.</returns>
    public override string ToString() => "[" + string.Join(", ", Read()) + "]";

    bool ICollection<T>.Contains(T item) => _items.Contains(item);

    int IList<T>.IndexOf(T item) => _items.IndexOf(item);

    void ICollection<T>.Add(T item) => Building().Add(item);

    void ICollection<T>.Clear() => Building().Clear();

    bool ICollection<T>.Remove(T item) => Building().Remove(item);

    void IList<T>.Insert(int index, T item) => Building().Insert(index, item);

    void IList<T>.RemoveAt(int index) => Building().RemoveAt(index);

    int IList.Add(object? value) => ((IList)Building()).Add(value);

    void IList.Clear() => Building().Clear();

    bool IList.Contains(object? value) => ((IList)_items).Contains(value);

    int IList.IndexOf(object? value) => ((IList)_items).IndexOf(value);

    void IList.Insert(int index, object? value) => ((IList)Building()).Insert(index, value);

    void IList.Remove(object? value) => ((IList)Building()).Remove(value);

    void IList.RemoveAt(int index) => Building().RemoveAt(index);

    void ICollection.CopyTo(Array array, int index) => ((ICollection)Read()).CopyTo(array, index);

    // Read once, frozen for good. EF Core fills a list before anybody reads it — one it made, or the `= []` a row's
    // constructor started with — and nobody changes it after.
    private List<T> Read()
    {
        _frozen = true;
        return _items;
    }

    private List<T> Building() =>
        _frozen
            ? throw new NotSupportedException("A ValueList is a value and never changes once made: make a new one — [.. list, item].")
            : _items;
}

/// <summary>Makes a <see cref="ValueList{T}" />.</summary>
public static class ValueList
{
    /// <summary>The list of <paramref name="items" />, in their order — what <c>[a, b]</c> calls.</summary>
    /// <typeparam name="T">What it holds.</typeparam>
    /// <param name="items">The elements.</param>
    /// <returns>The list.</returns>
    public static ValueList<T> Of<T>(ReadOnlySpan<T> items) => new([.. items]);

    /// <summary>The list of <paramref name="items" />, in their order.</summary>
    /// <typeparam name="T">What it holds.</typeparam>
    /// <param name="items">The elements.</param>
    /// <returns>The list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items" /> is null.</exception>
    public static ValueList<T> Of<T>(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new([.. items]);
    }
}
