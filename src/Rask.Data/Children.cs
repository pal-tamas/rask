using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Rask.Data;

/// <summary>
///     The children an aggregate holds, read-only to everyone else: the aggregate changes them by <c>Sync</c> from the rows
///     a form (or an import, or a message) says there should be, or by <see cref="Add" /> and <see cref="Remove" /> —
///     which only its own type may call (RASK180).
/// </summary>
/// <remarks>
///     <code>
/// public sealed class Supply : Aggregate&lt;SupplyId&gt;
/// {
///     public Children&lt;SupplyPart, SupplyPartSpec&gt; Parts { get; } = new();
///
///     public void Change(IReadOnlyList&lt;SupplyPartSpec&gt; parts) =&gt;
///         Parts.Sync(parts, s =&gt; s.Id, add: SupplyPart.For, update: (p, s) =&gt; p.Hold(s));
/// }
///     </code>
///     <para>
///         It is the collection EF Core maps — the property's own backing field — so the aggregate declares no private
///         <c>List&lt;T&gt;</c> and no public view of it. EF Core loads into it and tracks it as any collection of children:
///         a child <c>Sync</c> adds is inserted, one it changes is updated, one it lets go is deleted, all in the
///         save of the aggregate, and the save's domain step asks each its rules under its row — <c>Parts[1].Quantity</c>.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The child: an <see cref="Entity{TId}" />.</typeparam>
/// <typeparam name="TSpec">What one child is made from: a row of the form, a line of the import.</typeparam>
#pragma warning disable CA1710 // the name an aggregate reads as (Children<Chip, ChipPeriod> Chips), decided in the DDD plan; "ChildCollection" says the mechanism
public sealed class Children<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TEntity, TSpec>
#pragma warning restore CA1710
    : IReadOnlyList<TEntity>, ICollection<TEntity>
    where TEntity : class, IEntity
{
    private readonly List<TEntity> _held = [];

    /// <summary>How many children there are.</summary>
    public int Count => _held.Count;

    bool ICollection<TEntity>.IsReadOnly => false;

    /// <summary>The child at <paramref name="index" />, in the order the last <c>Sync</c> gave them.</summary>
    /// <param name="index">The position.</param>
    public TEntity this[int index] => _held[index];

    /// <summary>
    ///     Makes the children what <paramref name="specs" /> say, in their order: a spec whose key is a child's <c>Id</c>
    ///     updates that child, any other spec adds one, and a child no spec names is let go.
    /// </summary>
    /// <param name="specs">The children there should be.</param>
    /// <param name="key">
    ///     The <c>Id</c> of the child a spec stands for, of the children's own id type; <see langword="null" /> for a new one.
    /// </param>
    /// <param name="add">Makes the child of a spec that names none.</param>
    /// <param name="update">Brings a child in line with its spec.</param>
    /// <param name="remove">What a child does as it is let go — raise an event, release what it held; nothing when left out.</param>
    /// <returns>What changed: the children added, those changed, those let go.</returns>
    /// <exception cref="ArgumentException">
    ///     Two specs name the same key, or a key is not of the children's id type — an <c>int</c> for a typed id.
    /// </exception>
    public SyncResult<TEntity> Sync(
        IEnumerable<TSpec> specs,
        Func<TSpec, object?> key,
        Func<TSpec, TEntity> add,
        Action<TEntity, TSpec> update,
        Action<TEntity>? remove = null)
    {
        ArgumentNullException.ThrowIfNull(specs);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(update);

        // Every key is checked before any child is touched: a refused sync leaves the children as they were.
        var held = HeldBy(KeyOf);
        var rows = specs.Select(spec => (Spec: spec, Id: key(spec))).ToList();
        if (ForeignKey(rows, held) is { } foreign)
        {
            throw new ArgumentException(foreign, nameof(key));
        }

        if (NamedTwice(rows) is { } twice)
        {
            throw new ArgumentException(twice, nameof(specs));
        }

        return Replace(rows, held, add, update, remove);
    }

    /// <summary>
    ///     Makes the children what <paramref name="specs" /> say, matched by a key of their own rather than their <c>Id</c>
    ///     — a fuelling of a delivery note by its transaction: a spec whose key a child holds updates it, any other spec adds
    ///     one, and a child no spec names is let go.
    /// </summary>
    /// <typeparam name="TKey">The key both sides hold.</typeparam>
    /// <param name="specs">The children there should be.</param>
    /// <param name="specKey">The key of the child a spec stands for; <see langword="null" /> for a new one.</param>
    /// <param name="childKey">The key a child holds.</param>
    /// <param name="add">Makes the child of a spec no child holds the key of.</param>
    /// <param name="update">Brings a child in line with its spec.</param>
    /// <param name="remove">What a child does as it is let go; nothing when left out.</param>
    /// <returns>What changed: the children added, those changed, those let go.</returns>
    /// <exception cref="ArgumentException">Two specs name the same key, or two children hold one.</exception>
    public SyncResult<TEntity> Sync<TKey>(
        IEnumerable<TSpec> specs,
        Func<TSpec, TKey?> specKey,
        Func<TEntity, TKey> childKey,
        Func<TSpec, TEntity> add,
        Action<TEntity, TSpec> update,
        Action<TEntity>? remove = null)
    {
        ArgumentNullException.ThrowIfNull(specs);
        ArgumentNullException.ThrowIfNull(specKey);
        ArgumentNullException.ThrowIfNull(childKey);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(update);

        var rows = specs.Select(spec => (Spec: spec, Id: (object?)specKey(spec))).ToList();
        if (NamedTwice(rows) is { } twice)
        {
            throw new ArgumentException(twice, nameof(specs));
        }

        var held = new Dictionary<object, TEntity>();
        foreach (var child in _held)
        {
            if ((object?)childKey(child) is { } id && !held.TryAdd(id, child))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"Two {typeof(TEntity).Name} children hold the same key, '{id}'."),
                    nameof(childKey));
            }
        }

        return Replace(rows, held, add, update, remove);
    }

    /// <summary>Adds <paramref name="item" />: from the aggregate that holds these children, and only there (RASK180).</summary>
    /// <param name="item">The child.</param>
    public void Add(TEntity item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _held.Add(item);
    }

    /// <summary>Lets <paramref name="item" /> go: from the aggregate that holds these children, and only there (RASK180).</summary>
    /// <param name="item">The child.</param>
    /// <returns>Whether it was held.</returns>
    public bool Remove(TEntity item) => _held.Remove(item);

    // The specs' children in their order, and those no spec named let go.
    private SyncResult<TEntity> Replace(
        List<(TSpec Spec, object? Id)> rows,
        Dictionary<object, TEntity> held,
        Func<TSpec, TEntity> add,
        Action<TEntity, TSpec> update,
        Action<TEntity>? remove)
    {
        var (kept, added, changed, touched) = Apply(rows, held, add, update);
        var keptSet = new HashSet<TEntity>(kept, ReferenceEqualityComparer.Instance);
        var removed = _held.Where(child => !keptSet.Contains(child)).ToList();
        foreach (var child in removed)
        {
            remove?.Invoke(child);
        }

        _held.Clear();
        _held.AddRange(kept);
        return new SyncResult<TEntity>(added, changed, removed, touched);
    }

    // Each spec in its order: the child its key names updated, or a child made for it.
    private static (List<TEntity> Kept, List<TEntity> Added, List<TEntity> Changed, List<TEntity> Touched) Apply(
        List<(TSpec Spec, object? Id)> rows,
        Dictionary<object, TEntity> unmatched,
        Func<TSpec, TEntity> add,
        Action<TEntity, TSpec> update)
    {
        List<TEntity> kept = [], added = [], changed = [], touched = [];
        foreach (var (spec, id) in rows)
        {
            if (id is not null && unmatched.Remove(id, out var held))
            {
                var before = ChildState<TEntity>.Of(held);
                update(held, spec);
                kept.Add(held);
                if (!ChildState<TEntity>.Same(before, ChildState<TEntity>.Of(held)))
                {
                    changed.Add(held);
                    touched.Add(held);
                }

                continue;
            }

            var made = add(spec) ?? throw new InvalidOperationException($"Sync's add made no {typeof(TEntity).Name} of a spec.");
            kept.Add(made);
            added.Add(made);
            touched.Add(made);
        }

        return (kept, added, changed, touched);
    }

    private Dictionary<object, TEntity> HeldBy(Func<TEntity, object?> key)
    {
        var held = new Dictionary<object, TEntity>();
        foreach (var child in _held)
        {
            if (key(child) is { } id)
            {
                held[id] = child;
            }
        }

        return held;
    }

    // A key of another type than the children's ids would match none of them and replace every child.
    private static string? ForeignKey(List<(TSpec Spec, object? Id)> rows, Dictionary<object, TEntity> held)
    {
        if (held.Keys.FirstOrDefault()?.GetType() is not { } idType ||
            rows.FirstOrDefault(r => r.Id is not null && r.Id.GetType() != idType).Id is not { } foreign)
        {
            return null;
        }

        return $"A spec's key is a {foreign.GetType().Name}, but the {typeof(TEntity).Name} children's ids are " +
               $"{idType.Name}s: return the child's own Id from the key.";
    }

    // A key named twice would update one child twice.
    private static string? NamedTwice(List<(TSpec Spec, object? Id)> rows)
    {
        var named = new HashSet<object>();
        return rows.FirstOrDefault(r => r.Id is not null && !named.Add(r.Id)).Id is { } twice
            ? string.Create(CultureInfo.InvariantCulture, $"Two specs name the same {typeof(TEntity).Name}, '{twice}'.")
            : null;
    }

    /// <inheritdoc />
    public IEnumerator<TEntity> GetEnumerator() => _held.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // What EF Core loads, fixes up and copies the children through (Add and Remove are the aggregate's own, above).
    void ICollection<TEntity>.Clear() => _held.Clear();

    bool ICollection<TEntity>.Contains(TEntity item) => _held.Contains(item);

    void ICollection<TEntity>.CopyTo(TEntity[] array, int arrayIndex) => _held.CopyTo(array, arrayIndex);

    private static object? KeyOf(TEntity child) => (child as IKeyedEntity)?.Key;
}
