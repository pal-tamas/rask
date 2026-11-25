using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     What one <see cref="Audited" /> aggregate's save records, before an <see cref="AuditFormat" /> writes it: the key,
///     the values that changed — and, for a format that asks for them, every value it held and holds.
/// </summary>
/// <remarks>
///     Values are read and compared as the database stores them. A value the database generates is not known until the
///     rows went out: <see cref="ReadTemporary" /> reads it into each list that recorded it.
/// </remarks>
internal sealed class AuditDraft
{
    private readonly List<(List<AuditValue> Values, int At, PropertyEntry Property)> _temporary = [];

    private AuditDraft(AuditAction action) => Action = action;

    /// <summary>What the save did to the aggregate.</summary>
    internal AuditAction Action { get; }

    /// <summary>The aggregate's key.</summary>
    internal List<AuditValue> Key { get; } = [];

    /// <summary>What changed, as it was: an insert none, a delete every value it held.</summary>
    internal List<AuditValue> Before { get; } = [];

    /// <summary>What changed, as it became: a delete none, an insert every value it stores.</summary>
    internal List<AuditValue> After { get; } = [];

    /// <summary>Every value the aggregate held as the save began — its rows' too; none for an insert. Read for a snapshot only.</summary>
    internal List<AuditValue> Was { get; } = [];

    /// <summary>Every value the aggregate holds once the save went out — its rows' too; none for a delete. Read for a snapshot only.</summary>
    internal List<AuditValue> Is { get; } = [];

    /// <summary>The paths whose value the save changed: every one of a row it added or deleted. Read for a snapshot only.</summary>
    internal HashSet<string> Changed { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether a value the database generates was recorded, so the draft is read again once the rows went out.</summary>
    internal bool HasTemporary => _temporary.Count > 0;

    /// <summary>The draft of <paramref name="root" />'s save.</summary>
    /// <param name="context">The context saving.</param>
    /// <param name="root">The aggregate.</param>
    /// <param name="written">Its rows the save writes, the root first when it is one of them.</param>
    /// <param name="held">Every tracked row of it, the root first — or null when no snapshot is read.</param>
    internal static AuditDraft Of(DbContext context, EntityEntry root, List<EntityEntry> written, List<EntityEntry>? held)
    {
        var draft = new AuditDraft(ActionOf(root));
        foreach (var key in (root.Metadata.FindPrimaryKey()?.Properties ?? []).Select(static k => k.Name))
        {
            draft.Add(draft.Key, key, root.Property(key), original: root.State == EntityState.Deleted);
        }

        foreach (var row in written)
        {
            draft.ReadChanges(row, PlaceOf(context, root, row), row.Properties, row.ComplexProperties);
        }

        foreach (var row in held ?? [])
        {
            draft.ReadSnapshot(row, PlaceOf(context, root, row), row.Properties, row.ComplexProperties);
        }

        return draft;
    }

    /// <summary>Reads the values the database generated into every list that recorded them.</summary>
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
    private static string PlaceOf(DbContext context, EntityEntry root, EntityEntry row)
    {
        if (ReferenceEquals(row.Entity, root.Entity))
        {
            return "";
        }

        var place = RowPath.Of(context, row);
        if (place.Length > 0)
        {
            return place;
        }

        var key = row.Metadata.FindPrimaryKey()?.Properties.Select(p => row.Property(p.Name).OriginalValue) ?? [];
        return row.Metadata.ClrType.Name + "[" + string.Join(",", key) + "].";
    }

    private void ReadChanges(EntityEntry row, string place, IEnumerable<PropertyEntry> properties, IEnumerable<ComplexPropertyEntry> complex)
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
                    if (IsChanged(property))
                    {
                        Add(Before, name, property, original: true);
                        Add(After, name, property, original: false);
                    }

                    break;
            }
        }

        foreach (var value in complex)
        {
            ReadChanges(row, place + value.Metadata.Name + ".", value.Properties, value.ComplexProperties);
        }
    }

    private void ReadSnapshot(EntityEntry row, string place, IEnumerable<PropertyEntry> properties, IEnumerable<ComplexPropertyEntry> complex)
    {
        foreach (var property in properties)
        {
            var name = place + property.Metadata.Name;
            if (row.State != EntityState.Added)
            {
                Add(Was, name, property, original: true);
            }

            if (row.State != EntityState.Deleted)
            {
                Add(Is, name, property, original: false);
            }

            if (row.State is EntityState.Added or EntityState.Deleted || IsChanged(property))
            {
                Changed.Add(name);
            }
        }

        foreach (var value in complex)
        {
            ReadSnapshot(row, place + value.Metadata.Name + ".", value.Properties, value.ComplexProperties);
        }
    }

    private static bool IsChanged(PropertyEntry property) =>
        !Same(Stored(property.Metadata, property.OriginalValue), Stored(property.Metadata, property.CurrentValue));

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
