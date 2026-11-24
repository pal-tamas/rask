using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     The <see cref="AuditEntry" /> each <see cref="Audited" /> aggregate a save writes leaves, added to that same save:
///     one per aggregate, its children's rows within it under their place (<c>Lines[1].Quantity</c>).
/// </summary>
/// <remarks>
///     <para>
///         Read from what the context tracks as the save starts, after every other step stamped, versioned or rewrote it:
///         an insert records each value it stores, a delete each it held, an update the ones that changed — a value that
///         became null or stopped being null among them. Values are compared and recorded as the database stores them.
///     </para>
///     <para>
///         A key the database generates is not known until the rows went out: an entry that recorded one is completed
///         once they did, before the save's transaction commits (<see cref="SaveFollowUps" />).
///     </para>
/// </remarks>
internal static class AuditTrail
{
    // The entries a save added, taken back out of the context once it is written.
    private static readonly ConditionalWeakTable<DbContext, List<EntityEntry>> Added = [];

    internal static void Record(DbContext context, AuditFormat format, DateTime at)
    {
        if (!AuditEntries.AreMapped(context.Model))
        {
            return;
        }

        // An entry is written, never read back through the context: what an earlier save left in it — written, or added
        // by a save that failed — is not this save's.
        foreach (var left in context.ChangeTracker.Entries<AuditEntry>().ToList())
        {
            left.State = EntityState.Detached;
        }

        List<(EntityEntry Root, List<EntityEntry> Rows)> aggregates;
        using (TrackedPrincipals.During(context))
        {
            aggregates = Written(context);
        }

        if (aggregates.Count == 0)
        {
            return;
        }

        var user = CurrentUser.Id is { } id ? CurrentUser.ToClaim(id) : AuditEntry.System;
        var added = new List<EntityEntry>(aggregates.Count);
        foreach (var (root, rows) in aggregates)
        {
            var draft = Draft.Of(context, root, rows);
            var entry = context.Add(new AuditEntry(
                root.Metadata.ClrType.Name,
                draft.Action,
                format.Values(draft.Key),
                draft.Action == AuditAction.Insert ? null : format.Values(draft.Before),
                draft.Action == AuditAction.Delete ? null : format.Values(draft.After),
                user,
                at));
            added.Add(entry);
            if (draft.HasTemporary)
            {
                SaveFollowUps.Add(context, cancellationToken => Complete(context, entry, draft, format, cancellationToken));
            }
        }

        Added.AddOrUpdate(context, added);
    }

    /// <summary>The save went out: its entries are written, and the context lets go of them.</summary>
    internal static void Saved(DbContext? context)
    {
        if (context is null || !Added.TryGetValue(context, out var added))
        {
            return;
        }

        Added.Remove(context);
        foreach (var entry in added)
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    ///     The save failed or never started. Its entries stay until the next save takes them out: EF Core is still putting
    ///     back the values a failed save generated, and an entry let go of now would come back into its identity map.
    /// </summary>
    internal static void Failed(DbContext? context)
    {
        if (context is not null)
        {
            Added.Remove(context);
        }
    }

    // Each written row of an Audited aggregate, under its root — the root itself first when it was written. Keyed by the
    // root entity: the tracker hands out a new EntityEntry each time it is asked.
    private static List<(EntityEntry Root, List<EntityEntry> Rows)> Written(DbContext context)
    {
        var aggregates = new Dictionary<object, (EntityEntry Root, List<EntityEntry> Rows)>(ReferenceEqualityComparer.Instance);
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted) || entry.Entity is not IEntity)
            {
                continue;
            }

            var root = entry.Entity is IAggregate ? entry : AuditingInterceptor.RootOf(context, entry);
            if (root is null || !ConventionRegistry.Has(root.Metadata.ClrType, TraitSet.Audited))
            {
                continue;
            }

            if (!aggregates.TryGetValue(root.Entity, out var aggregate))
            {
                aggregate = (root, []);
                aggregates.Add(root.Entity, aggregate);
            }

            aggregate.Rows.Add(entry);
        }

        return [.. aggregates.Values];
    }

    // The values the database generated are read now, and the entry is written again with them.
    private static async Task Complete(DbContext context, EntityEntry entry, Draft draft, AuditFormat format, CancellationToken cancellationToken)
    {
        if (entry.State == EntityState.Detached)
        {
            return;
        }

        draft.ReadTemporary();
        var audit = (AuditEntry)entry.Entity;
        var key = format.Values(draft.Key);
        var after = draft.Action == AuditAction.Delete ? null : format.Values(draft.After);
        await context.Set<AuditEntry>().Where(a => a.Id == audit.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Key, key).SetProperty(a => a.After, after), cancellationToken)
            .ConfigureAwait(false);

        // The tracked entry says what the row now holds, unchanged.
        audit.Key = key;
        audit.After = after;
        entry.OriginalValues[nameof(AuditEntry.Key)] = key;
        entry.OriginalValues[nameof(AuditEntry.After)] = after;
    }

    /// <summary>What one aggregate's entry records, before the format writes it.</summary>
    private sealed class Draft
    {
        private readonly List<(List<AuditValue> Values, int At, PropertyEntry Property)> _temporary = [];

        private Draft(AuditAction action) => Action = action;

        internal AuditAction Action { get; }

        internal List<AuditValue> Key { get; } = [];

        internal List<AuditValue> Before { get; } = [];

        internal List<AuditValue> After { get; } = [];

        internal bool HasTemporary => _temporary.Count > 0;

        internal static Draft Of(DbContext context, EntityEntry root, List<EntityEntry> rows)
        {
            var draft = new Draft(ActionOf(root));
            foreach (var key in (root.Metadata.FindPrimaryKey()?.Properties ?? []).Select(static k => k.Name))
            {
                draft.Add(draft.Key, key, root.Property(key), original: root.State == EntityState.Deleted);
            }

            foreach (var row in rows)
            {
                draft.Read(row, ReferenceEquals(row.Entity, root.Entity) ? "" : PlaceOf(context, row), row.Properties, row.ComplexProperties);
            }

            return draft;
        }

        internal void ReadTemporary()
        {
            foreach (var (values, at, property) in _temporary)
            {
                values[at] = values[at] with { Value = Stored(property.Metadata, property.CurrentValue) };
            }
        }

        // A soft delete went out as an update that stamped DeletedAt: it is the aggregate's delete.
        private static AuditAction ActionOf(EntityEntry root) =>
            root.State switch
            {
                EntityState.Added => AuditAction.Insert,
                EntityState.Deleted => AuditAction.Delete,
                _ when root.Metadata.FindProperty(Columns.DeletedAt) is not null &&
                       root.Property(Columns.DeletedAt) is { OriginalValue: null, CurrentValue: not null } => AuditAction.Delete,
                _ => AuditAction.Update,
            };

        // Where the row sits in its aggregate, as its form names it; a row the save deletes holds no place there any more,
        // and is named by its type and key.
        private static string PlaceOf(DbContext context, EntityEntry row)
        {
            var place = RowPath.Of(context, row);
            if (place.Length > 0)
            {
                return place;
            }

            var key = row.Metadata.FindPrimaryKey()?.Properties.Select(p => row.Property(p.Name).OriginalValue) ?? [];
            return row.Metadata.ClrType.Name + "[" + string.Join(",", key) + "].";
        }

        private void Read(EntityEntry row, string place, IEnumerable<PropertyEntry> properties, IEnumerable<ComplexPropertyEntry> complex)
        {
            foreach (var property in properties)
            {
                var name = place + property.Metadata.Name;
                switch (row.State)
                {
                    case EntityState.Added:
                        Add(After, name, property, original: false);
                        break;
                    case EntityState.Deleted:
                        Add(Before, name, property, original: true);
                        break;
                    default:
                        if (!Same(Stored(property.Metadata, property.OriginalValue), Stored(property.Metadata, property.CurrentValue)))
                        {
                            Add(Before, name, property, original: true);
                            Add(After, name, property, original: false);
                        }

                        break;
                }
            }

            foreach (var value in complex)
            {
                Read(row, place + value.Metadata.Name + ".", value.Properties, value.ComplexProperties);
            }
        }

        private void Add(List<AuditValue> values, string name, PropertyEntry property, bool original)
        {
            if (!original && property.IsTemporary)
            {
                _temporary.Add((values, values.Count, property));
            }

            values.Add(new AuditValue(name, Stored(property.Metadata, original ? property.OriginalValue : property.CurrentValue)));
        }

        // A value object as the value it is stored as; a plain value as it is, never in a provider's own spelling — so
        // every database records the same text.
        private static object? Stored(IProperty property, object? value) =>
            value is null || IsPlain(value.GetType()) || (property.GetValueConverter() ?? property.GetTypeMapping().Converter) is not { } converter
                ? value
                : converter.ConvertToProvider(value);

        private static bool IsPlain(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) ||
            type == typeof(TimeSpan) || type == typeof(byte[]);

        private static bool Same(object? before, object? after) =>
            before is byte[] one && after is byte[] other ? one.AsSpan().SequenceEqual(other) : Equals(before, after);
    }
}
