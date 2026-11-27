using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Rask.Data;

/// <summary>
///     The children an aggregate holds, read by everyone and changed only by the aggregate itself (RASK180): it adds
///     and removes them one by one, or makes them what a form (an import, a message) says they should be with
///     <c>Sync</c>.
/// </summary>
/// <remarks>
///     <code>
/// public sealed class Supply : Aggregate&lt;SupplyId&gt;
/// {
///     public HasMany&lt;SupplyPart&gt; Parts { get; } = new();
///
///     public void Change(IReadOnlyList&lt;SupplyPartSpec&gt; parts) =&gt;
///         Parts.Sync(parts, s =&gt; s.Id, add: SupplyPart.For, update: (p, s) =&gt; p.Hold(s));
/// }
///     </code>
///     <para>
///         It is the collection EF Core maps — the property's own backing field — so the aggregate declares no private
///         <c>List&lt;T&gt;</c> and no read-only view of it, and moving from that pair to a <c>HasMany</c> is no
///         migration: the navigation keeps its name and the child its foreign key. EF Core loads into it and tracks it as
///         any collection of children: a child added is inserted, one changed is updated, one let go is deleted, all in
///         the save of the aggregate, and the save's domain step asks each its rules under its row —
///         <c>Parts[1].Quantity</c>.
///     </para>
///     <para>
///         A store keeps no order of its own, so the children load in whatever order it returns them. Where the order
///         is the data — the lines of a document — say so in the aggregate's <c>Configure</c>:
///         <c>b.InOrder(s =&gt; s.Parts)</c> stores each child's place (<see cref="Columns.Position" />) and loads them
///         back in it.
///     </para>
/// </remarks>
/// <typeparam name="TChild">The child: an <see cref="Entity{TId}" /> that is not an aggregate.</typeparam>
#pragma warning disable CA1710 // the name an aggregate reads as (HasMany<Pistol> Pistols), decided in the relationships plan; "ChildCollection" says the mechanism
public sealed class HasMany<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TChild>
#pragma warning restore CA1710
    : IReadOnlyList<TChild>, ICollection<TChild>
    where TChild : class, IEntity
{
    private readonly List<TChild> _held = [];

    // Where each child EF Core loaded was stored, while it loads them in: what Add orders them by (InOrder only).
    private Dictionary<TChild, int>? _loaded;

    /// <summary>How many children there are.</summary>
    public int Count => _held.Count;

    bool ICollection<TChild>.IsReadOnly => false;

    /// <summary>The child at <paramref name="index" />.</summary>
    /// <param name="index">The position.</param>
    public TChild this[int index] => _held[index];

    /// <summary>Adds <paramref name="item" />: from the aggregate that holds these children, and only there (RASK180).</summary>
    /// <param name="item">The child.</param>
    public void Add(TChild item)
    {
        ArgumentNullException.ThrowIfNull(item);

        // A child EF Core loads goes where it was stored (Columns.Position, under InOrder), whatever order the rows came back in.
        if (ChildPositions.Take(item) is { } position)
        {
            _loaded ??= new Dictionary<TChild, int>(ReferenceEqualityComparer.Instance);
            var at = _held.FindIndex(held => _loaded.TryGetValue(held, out var other) && other > position);
            _loaded[item] = position;
            _held.Insert(at < 0 ? _held.Count : at, item);
            return;
        }

        _held.Add(item);
    }

    /// <summary>Adds each of <paramref name="children" />, in their order: from the aggregate, and only there (RASK180).</summary>
    /// <param name="children">The children.</param>
    public void Add(IEnumerable<TChild> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        foreach (var child in children.ToList())
        {
            Add(child);
        }
    }

    /// <summary>Lets <paramref name="item" /> go — it is deleted in the aggregate's save: from the aggregate, and only there (RASK180).</summary>
    /// <param name="item">The child.</param>
    /// <returns>Whether it was held.</returns>
    public bool Remove(TChild item) => _held.Remove(item);

    /// <summary>Lets go of every child <paramref name="match" /> picks: from the aggregate, and only there (RASK180).</summary>
    /// <param name="match">Which children go.</param>
    /// <returns>How many went.</returns>
    public int RemoveAll(Func<TChild, bool> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return _held.RemoveAll(child => match(child));
    }

    /// <summary>Lets go of every child: from the aggregate, and only there (RASK180).</summary>
    public void Clear()
    {
        _held.Clear();
        _loaded = null;
    }

    /// <summary>
    ///     Makes the children what <paramref name="specs" /> say, in their order: a spec whose key is a child's <c>Id</c>
    ///     updates that child, any other spec adds one, and a child no spec names is let go.
    /// </summary>
    /// <typeparam name="TSpec">What one child is made from: a row of the form, a line of the import.</typeparam>
    /// <param name="specs">The children there should be.</param>
    /// <param name="key">
    ///     The <c>Id</c> of the child a spec stands for, of the children's own id type; <see langword="null" /> for a new one.
    /// </param>
    /// <param name="add">Makes the child of a spec that names none.</param>
    /// <param name="update">Brings a child in line with its spec.</param>
    /// <param name="remove">What a child does as it is let go — raise an event, release what it held; nothing when left out.</param>
    /// <returns>What changed: the children added, those changed, those let go.</returns>
    /// <exception cref="ArgumentException">
    ///     Two specs name the same key — the specs are refused rather than one child updated twice; or a key is not of the
    ///     children's id type (an <c>int</c> for a typed id).
    /// </exception>
    public SyncResult<TChild> Sync<TSpec>(
        IEnumerable<TSpec> specs,
        Func<TSpec, object?> key,
        Func<TSpec, TChild> add,
        Action<TChild, TSpec> update,
        Action<TChild>? remove = null)
    {
        ArgumentNullException.ThrowIfNull(specs);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(update);

        // Every key is checked before any child is touched: a refused sync leaves the children as they were.
        var held = HeldBy(static child => (child as IKeyedEntity)?.Key);
        var rows = specs.Select(spec => (Spec: spec, Id: key(spec))).ToList();
        if (ForeignKey(rows, held) is { } foreign)
        {
            throw new ArgumentException(foreign, nameof(key));
        }

        ThrowOnKeyNamedTwice(rows, nameof(specs));
        return Replace(rows, held, add, update, remove);
    }

    /// <summary>
    ///     Makes the children what <paramref name="specs" /> say, matched by a key of their own rather than their <c>Id</c>
    ///     — a fuelling of a delivery note by its transaction: a spec whose key a child holds updates it, any other spec adds
    ///     one, and a child no spec names is let go.
    /// </summary>
    /// <typeparam name="TSpec">What one child is made from.</typeparam>
    /// <typeparam name="TKey">The key both sides hold.</typeparam>
    /// <param name="specs">The children there should be.</param>
    /// <param name="specKey">The key of the child a spec stands for; <see langword="null" /> for a new one.</param>
    /// <param name="childKey">The key a child holds.</param>
    /// <param name="add">Makes the child of a spec no child holds the key of.</param>
    /// <param name="update">Brings a child in line with its spec.</param>
    /// <param name="remove">What a child does as it is let go; nothing when left out.</param>
    /// <returns>What changed: the children added, those changed, those let go.</returns>
    /// <exception cref="ArgumentException">Two specs name the same key, or two children hold one.</exception>
    public SyncResult<TChild> Sync<TSpec, TKey>(
        IEnumerable<TSpec> specs,
        Func<TSpec, TKey?> specKey,
        Func<TChild, TKey> childKey,
        Func<TSpec, TChild> add,
        Action<TChild, TSpec> update,
        Action<TChild>? remove = null)
    {
        ArgumentNullException.ThrowIfNull(specs);
        ArgumentNullException.ThrowIfNull(specKey);
        ArgumentNullException.ThrowIfNull(childKey);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(update);

        var rows = specs.Select(spec => (Spec: spec, Id: (object?)specKey(spec))).ToList();
        ThrowOnKeyNamedTwice(rows, nameof(specs));
        return Replace(rows, HeldOnce(childKey), add, update, remove);
    }

    /// <summary>
    ///     Makes a link what <paramref name="ids" /> say — the children that each name one row of another aggregate, a
    ///     driver's vehicles: a child whose key is among the ids stays, an id no child holds adds one, and a child whose
    ///     key is not among them is let go.
    /// </summary>
    /// <typeparam name="TKey">The other side's id.</typeparam>
    /// <param name="ids">The ids there should be links to, in their order.</param>
    /// <param name="childKey">The id a child links to.</param>
    /// <param name="add">Makes the child that links to an id.</param>
    /// <returns>What changed: the children added and those let go; a link is never changed in place.</returns>
    /// <exception cref="ArgumentException">An id is given twice or is <see langword="null" />, or two children hold one.</exception>
    public SyncResult<TChild> Sync<TKey>(IEnumerable<TKey> ids, Func<TChild, TKey> childKey, Func<TKey, TChild> add)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(childKey);
        ArgumentNullException.ThrowIfNull(add);

        var rows = ids.Select(static id => (Spec: id, Id: (object?)id)).ToList();
        if (rows.Exists(static row => row.Id is null))
        {
            throw new ArgumentException($"A link to {typeof(TChild).Name} names the other side's id; one of the ids is null.", nameof(ids));
        }

        ThrowOnKeyNamedTwice(rows, nameof(ids));
        return Replace(rows, HeldOnce(childKey), add, static (_, _) => { }, remove: null);
    }

    /// <inheritdoc />
    public IEnumerator<TChild> GetEnumerator() => _held.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool ICollection<TChild>.Contains(TChild item) => _held.Contains(item);

    void ICollection<TChild>.CopyTo(TChild[] array, int arrayIndex) => _held.CopyTo(array, arrayIndex);

    // The specs' children in their order, and those no spec named let go.
    private SyncResult<TChild> Replace<TSpec>(
        List<(TSpec Spec, object? Id)> rows,
        Dictionary<object, TChild> held,
        Func<TSpec, TChild> add,
        Action<TChild, TSpec> update,
        Action<TChild>? remove)
    {
        var (kept, added, changed, touched) = Apply(rows, held, add, update);
        var keptSet = new HashSet<TChild>(kept, ReferenceEqualityComparer.Instance);
        var removed = _held.Where(child => !keptSet.Contains(child)).ToList();
        foreach (var child in removed)
        {
            remove?.Invoke(child);
        }

        _held.Clear();
        _held.AddRange(kept);
        _loaded = null;
        return new SyncResult<TChild>(added, changed, removed, touched);
    }

    // Each spec in its order: the child its key names updated, or a child made for it.
    private static (List<TChild> Kept, List<TChild> Added, List<TChild> Changed, List<TChild> Touched) Apply<TSpec>(
        List<(TSpec Spec, object? Id)> rows,
        Dictionary<object, TChild> unmatched,
        Func<TSpec, TChild> add,
        Action<TChild, TSpec> update)
    {
        List<TChild> kept = [], added = [], changed = [], touched = [];
        foreach (var (spec, id) in rows)
        {
            if (id is not null && unmatched.Remove(id, out var held))
            {
                var before = ChildState<TChild>.Of(held);
                update(held, spec);
                kept.Add(held);
                if (!ChildState<TChild>.Same(before, ChildState<TChild>.Of(held)))
                {
                    changed.Add(held);
                    touched.Add(held);
                }

                continue;
            }

            var made = add(spec) ?? throw new InvalidOperationException($"Sync's add made no {typeof(TChild).Name} of a spec.");
            kept.Add(made);
            added.Add(made);
            touched.Add(made);
        }

        return (kept, added, changed, touched);
    }

    private Dictionary<object, TChild> HeldBy(Func<TChild, object?> key)
    {
        var held = new Dictionary<object, TChild>();
        foreach (var child in _held)
        {
            if (key(child) is { } id)
            {
                held[id] = child;
            }
        }

        return held;
    }

    // The children by a key of their own, which two of them holding would leave one unmatched.
    private Dictionary<object, TChild> HeldOnce<TKey>(Func<TChild, TKey> childKey)
    {
        var held = new Dictionary<object, TChild>();
        foreach (var child in _held)
        {
            if ((object?)childKey(child) is { } id && !held.TryAdd(id, child))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"Two {typeof(TChild).Name} children hold the same key, '{id}'."),
                    nameof(childKey));
            }
        }

        return held;
    }

    // A key of another type than the children's ids would match none of them and replace every child.
    private static string? ForeignKey<TSpec>(List<(TSpec Spec, object? Id)> rows, Dictionary<object, TChild> held)
    {
        if (held.Keys.FirstOrDefault()?.GetType() is not { } idType ||
            rows.FirstOrDefault(r => r.Id is not null && r.Id.GetType() != idType).Id is not { } foreign)
        {
            return null;
        }

        return $"A spec's key is a {foreign.GetType().Name}, but the {typeof(TChild).Name} children's ids are " +
               $"{idType.Name}s: return the child's own Id from the key.";
    }

    // A key named twice would update one child twice, or link to one row twice.
    private static void ThrowOnKeyNamedTwice<TSpec>(List<(TSpec Spec, object? Id)> rows, string parameter)
    {
        var named = new HashSet<object>();
        if (rows.FirstOrDefault(r => r.Id is not null && !named.Add(r.Id)).Id is { } twice)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"Two specs name the same {typeof(TChild).Name}, '{twice}'."), parameter);
        }
    }
}
