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
    private readonly Dictionary<DbContext, List<EntityEntry>> _pending = [];

    /// <summary>Whether the warning <see cref="AggregatePolicy.Warn" /> logs has been logged for this transaction.</summary>
    internal bool Warned { get; set; }

    /// <summary>Every aggregate written so far, as <c>Type key</c>, in the order first written.</summary>
    internal IReadOnlyList<string> All => _written;

    /// <summary>
    ///     Adds the aggregates <paramref name="context" />'s pending save writes, as its <c>SavingChanges</c> sees them.
    ///     A row whose key the store has yet to make is named once the save has made it (<see cref="Settle" />): its
    ///     temporary key would make it a second aggregate when it is saved again.
    /// </summary>
    /// <returns>True when the save writes one not counted before.</returns>
    internal bool Stage(DbContext context)
    {
        var added = false;
        List<EntityEntry>? later = null;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (HasTemporaryKey(entry))
            {
                (later ??= []).Add(entry);
            }
            else
            {
                added |= Count(context, entry);
            }
        }

        if (later is not null)
        {
            _pending[context] = later;
        }

        return added;
    }

    /// <summary>Names the rows <see cref="Stage" /> left for after the save, their keys made by now.</summary>
    /// <returns>True when one was an aggregate not counted before.</returns>
    internal bool Settle(DbContext context)
    {
        if (!_pending.Remove(context, out var later))
        {
            return false;
        }

        var added = false;
        foreach (var entry in later)
        {
            added |= Count(context, entry);
        }

        return added;
    }

    /// <summary>Forgets what <see cref="Stage" /> left for a save that failed.</summary>
    internal void Abandon(DbContext context) => _pending.Remove(context);

    private bool Count(DbContext context, EntityEntry entry)
    {
        if (RootOf(context, entry, 0) is not { } root || !_seen.Add(root))
        {
            return false;
        }

        _written.Add(root);
        return true;
    }

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
}
