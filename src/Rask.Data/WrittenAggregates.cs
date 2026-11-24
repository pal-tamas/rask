using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     The aggregates one <c>Db.Transaction</c> has written, each once: a saved child counts as its aggregate, and a
///     table of infrastructure (<see cref="InfrastructureBuilderExtensions.Infrastructure{TEntity}" />) not at all.
/// </summary>
internal sealed class WrittenAggregates
{
    // How far up a child's parents are followed before it is taken as its own: an aggregate deeper than this is
    // several wearing one name (AggregateChildren stops at the same depth).
    private const int MaxDepth = 4;

    private readonly List<string> _written = [];
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Dictionary<DbContext, List<Staged>> _pending = [];

    /// <summary>Whether the warning <see cref="AggregatePolicy.Warn" /> logs has been logged for this transaction.</summary>
    internal bool Warned { get; set; }

    /// <summary>Every aggregate written so far, as <c>Type key</c>, in the order first written.</summary>
    internal IReadOnlyList<string> All => _written;

    /// <summary>
    ///     Notes the aggregates <paramref name="context" />'s save is about to write, as its <c>SavingChanges</c> sees
    ///     them; they count once it has written them (<see cref="Settle" />), so a save refused for another reason counts
    ///     nothing. A row whose key the store has yet to make is named after the save: its temporary key would make it a
    ///     second aggregate when it is saved again. A deleted row is named now, while it is still tracked.
    /// </summary>
    internal void Stage(DbContext context)
    {
        List<Staged> staged =
        [
            .. context.ChangeTracker.Entries()
                .Where(static e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(e => HasTemporaryKey(e) ? new Staged(null, e) : new Staged(RootOf(context, e, 0), null)),
        ];

        if (staged.Count > 0)
        {
            _pending[context] = staged;
        }
    }

    /// <summary>Counts what <see cref="Stage" /> noted, the save having written it.</summary>
    /// <returns>True when it wrote an aggregate not counted before.</returns>
    internal bool Settle(DbContext context)
    {
        if (!_pending.Remove(context, out var staged))
        {
            return false;
        }

        var added = false;
        foreach (var (root, later) in staged)
        {
            if ((root ?? (later is null ? null : RootOf(context, later, 0))) is { } named && _seen.Add(named))
            {
                _written.Add(named);
                added = true;
            }
        }

        return added;
    }

    /// <summary>
    ///     Counts a set write (<c>UpdateWhere</c>, <c>DeleteWhere</c>) of <paramref name="type" />'s rows: one write of the
    ///     set, named <c>Pairing (set)</c>, whatever rows it touched; a table of infrastructure not at all.
    /// </summary>
    /// <returns>True when it is a write not counted before.</returns>
    internal bool NoteSet(IEntityType type)
    {
        if (IsInfrastructure(type))
        {
            return false;
        }

        var named = type.ClrType.Name + " (set)";
        if (!_seen.Add(named))
        {
            return false;
        }

        _written.Add(named);
        return true;
    }

    /// <summary>Forgets what <see cref="Stage" /> noted for a save that failed.</summary>
    internal void Abandon(DbContext context) => _pending.Remove(context);

    // An Added row, or the child of one, whose key the store makes: its parent's key is still temporary too.
    private static bool HasTemporaryKey(EntityEntry entry) =>
        entry.State == EntityState.Added && entry.Properties.Any(static p => p.IsTemporary);

    // `Supply 42` for the aggregate the entry belongs to; null for a table that is no aggregate's.
    private static string? RootOf(DbContext context, EntityEntry entry, int depth)
    {
        var type = entry.Metadata;
        if (IsInfrastructure(type))
        {
            return null;
        }

        if (typeof(IAggregate).IsAssignableFrom(type.ClrType) || depth >= MaxDepth || ParentOf(type) is not { } parent)
        {
            return type.FindPrimaryKey() is { } key ? Named(type, Values(entry, key.Properties)) : null;
        }

        var parentKey = Values(entry, parent.Properties);
        if (typeof(IAggregate).IsAssignableFrom(parent.PrincipalEntityType.ClrType))
        {
            return IsInfrastructure(parent.PrincipalEntityType) ? null : Named(parent.PrincipalEntityType, parentKey);
        }

        // A grandchild: its parent's own parent, from the parent row the save tracks — or the parent taken as its own.
        return Tracked(context, parent.PrincipalEntityType, parentKey) is { } principal
            ? RootOf(context, principal, depth + 1)
            : Named(parent.PrincipalEntityType, parentKey);
    }

    // The foreign key to the row that holds this one: the owner of an owned type, or the parent whose navigation
    // lists it (Supply.Parts) — not a reference by id to another aggregate, which has no navigation back.
    private static IForeignKey? ParentOf(IEntityType type) =>
        type.FindOwnership() ??
        type.GetForeignKeys().FirstOrDefault(static key =>
            key.PrincipalToDependent is not null && typeof(IEntity).IsAssignableFrom(key.PrincipalEntityType.ClrType));

    private static EntityEntry? Tracked(DbContext context, IEntityType type, string key) =>
        type.FindPrimaryKey() is { } primary
            ? context.ChangeTracker.Entries().FirstOrDefault(e =>
                e.Metadata == type && string.Equals(Values(e, primary.Properties), key, StringComparison.Ordinal))
            : null;

    private static bool IsInfrastructure(IEntityType type) =>
        InfrastructureBuilderExtensions.IsInfrastructure(type.ClrType) ||
        (type.FindOwnership() is { } ownership && IsInfrastructure(ownership.PrincipalEntityType));

    private static string Named(IEntityType type, string key) => type.ClrType.Name + " " + key;

    // A deleted row's key as it was read; any other's as it stands, a store-generated one still temporary.
    private static string Values(EntityEntry entry, IReadOnlyList<IProperty> properties) =>
        string.Join(
            "/",
            properties.Select(p =>
            {
                var property = entry.Property(p.Name);
                var value = entry.State == EntityState.Deleted ? property.OriginalValue : property.CurrentValue;
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
            }));

    // A row the save writes: named before it, or (its key still to be made) the entry to name after it.
    private readonly record struct Staged(string? Root, EntityEntry? Later);
}
