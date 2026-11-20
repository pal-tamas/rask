using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     The tracked row a foreign key names, found by its key values: what the save checks walk from a child to its parent
///     and on to its aggregate root (<see cref="AuditingInterceptor.RootOf" />, <see cref="ParentTenantCheck" />).
/// </summary>
/// <remarks>
///     <para>
///         Asked once a changed child, and more for a nested one, so a search of every tracked row each time made a save
///         cost the changed children times the rows tracked: an aggregate of 2,000 changed children took 19 s to save,
///         and one of tens of thousands ran for over ten minutes (Eszaf, 2026-11-20). While a save runs (<see cref="During" />)
///         the rows are indexed by every key they have, once, when the first parent is asked for, and each answer is then
///         a lookup.
///     </para>
///     <para>
///         The index never answers otherwise than the search did. A row it finds is checked against its key's values as
///         they are now; a row whose key the save itself changes — the tenant it stamps, the tenant key it keeps in step
///         (<see cref="Moved" />) — is indexed again by its new values. Outside a save every question is the search.
///     </para>
/// </remarks>
internal static class TrackedPrincipals
{
    private static readonly ConditionalWeakTable<DbContext, Index> Saving = [];

    /// <summary>Indexes <paramref name="context" />'s rows for the save about to run, until the returned scope ends.</summary>
    /// <param name="context">The context being saved.</param>
    /// <returns>The scope: disposed, the index is let go.</returns>
    internal static Scope During(DbContext context)
    {
        Saving.AddOrUpdate(context, new Index(context));
        return new Scope(context);
    }

    /// <summary>
    ///     The tracked row of <paramref name="key" />'s principal type whose principal key holds <paramref name="values" />
    ///     now; null when the context tracks none.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="key">The foreign key from the child.</param>
    /// <param name="values">The child's values of it, none of them null.</param>
    internal static EntityEntry? Find(DbContext context, IForeignKey key, object?[] values)
    {
        if (!Saving.TryGetValue(context, out var index))
        {
            return Search(context, key, values);
        }

        return index.Indexed(key.PrincipalKey, values) is { } found && Holds(found, key, values) ? found : null;
    }

    /// <summary>Indexes <paramref name="entry" /> again, by its keys' values now: the save has just written one of them.</summary>
    /// <param name="context">The context being saved.</param>
    /// <param name="entry">The row whose key may have changed.</param>
    internal static void Moved(DbContext context, EntityEntry entry)
    {
        if (Saving.TryGetValue(context, out var index))
        {
            index.Reindex(entry);
        }
    }

    // The tracked row of the principal's type whose principal key holds the values: the first, as the tracker lists them.
    private static EntityEntry? Search(DbContext context, IForeignKey key, object?[] values) =>
        context.ChangeTracker.Entries().FirstOrDefault(candidate => Holds(candidate, key, values));

    private static bool Holds(EntityEntry candidate, IForeignKey key, object?[] values)
    {
        if (!key.PrincipalEntityType.ClrType.IsInstanceOfType(candidate.Entity))
        {
            return false;
        }

        for (var i = 0; i < values.Length; i++)
        {
            if (!Equals(candidate.Property(key.PrincipalKey.Properties[i].Name).CurrentValue, values[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The save's index, let go when the save is done.</summary>
    internal readonly struct Scope(DbContext context) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose() => Saving.Remove(context);
    }

    // Every tracked row by each of its keys and that key's values, built at the first question; the first row the tracker
    // lists for a key's values wins, as the search finds it first.
    private sealed class Index(DbContext context)
    {
        private Dictionary<Lookup, EntityEntry>? _rows;

        // The lookups each row was indexed by, so a row the save moves is taken out from under its old values.
        private readonly Dictionary<object, List<Lookup>> _byRow = new(ReferenceEqualityComparer.Instance);

        internal EntityEntry? Indexed(IReadOnlyKey key, object?[] values) =>
            Rows().TryGetValue(new Lookup(key, values), out var found) ? found : null;

        internal void Reindex(EntityEntry entry)
        {
            if (_rows is null)
            {
                return;
            }

            if (_byRow.Remove(entry.Entity, out var before))
            {
                foreach (var lookup in before)
                {
                    if (_rows.TryGetValue(lookup, out var held) && ReferenceEquals(held.Entity, entry.Entity))
                    {
                        _rows.Remove(lookup);
                    }
                }
            }

            Add(_rows, entry);
        }

        private Dictionary<Lookup, EntityEntry> Rows()
        {
            if (_rows is { } rows)
            {
                return rows;
            }

            rows = [];
            foreach (var entry in context.ChangeTracker.Entries())
            {
                Add(rows, entry);
            }

            return _rows = rows;
        }

        private void Add(Dictionary<Lookup, EntityEntry> rows, EntityEntry entry)
        {
            var lookups = new List<Lookup>(1);
            foreach (var key in entry.Metadata.GetKeys())
            {
                if (ValuesOf(entry, key) is { } values && rows.TryAdd(new Lookup(key, values), entry))
                {
                    lookups.Add(new Lookup(key, values));
                }
            }

            _byRow[entry.Entity] = lookups;
        }

        private static object?[]? ValuesOf(EntityEntry entry, IReadOnlyKey key)
        {
            var values = new object?[key.Properties.Count];
            for (var i = 0; i < values.Length; i++)
            {
                if ((values[i] = entry.Property(key.Properties[i].Name).CurrentValue) is null)
                {
                    return null;
                }
            }

            return values;
        }
    }

    // A key and its values, equal as the search compares them: the same key, each value Equals the other.
    private readonly struct Lookup(IReadOnlyKey key, object?[] values) : IEquatable<Lookup>
    {
        private readonly IReadOnlyKey _key = key;

        private readonly object?[] _values = values;

        public bool Equals(Lookup other)
        {
            if (!ReferenceEquals(_key, other._key) || _values.Length != other._values.Length)
            {
                return false;
            }

            for (var i = 0; i < _values.Length; i++)
            {
                if (!Equals(_values[i], other._values[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is Lookup other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_key);
            foreach (var value in _values)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }
}
