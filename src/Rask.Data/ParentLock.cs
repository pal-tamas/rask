using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Wire;

namespace Rask.Data;

/// <summary>One declared lock (<see cref="LockedByBuilderExtensions" />): the property holding the parent, and what locks it.</summary>
/// <param name="property">The row's property holding its parent's key.</param>
internal abstract class ParentLock(string property)
{
    /// <summary>The row's property holding its parent's key.</summary>
    internal string Property { get; } = property;

    /// <summary>The failures of the <paramref name="writes" /> whose old or new parent is locked.</summary>
    internal abstract Task<IReadOnlyList<FieldFailure>> Check(
        DbContext context, IReadOnlyList<LockedWrite> writes, CancellationToken cancellationToken);
}

/// <summary>A lock whose parent is a <typeparamref name="TParent" />.</summary>
/// <typeparam name="TParent">The parent.</typeparam>
internal sealed class ParentLock<[DynamicallyAccessedMembers(DataTrimming.Entity)] TParent>(
    string property,
    Expression<Func<TParent, bool>> locked,
    Func<TParent, string> message,
    bool forUpdate) : ParentLock(property)
    where TParent : class
{
    // Parents read per query: one parameter each, well inside SQL Server's 2100.
    private const int PerQuery = 500;

    internal override async Task<IReadOnlyList<FieldFailure>> Check(
        DbContext context, IReadOnlyList<LockedWrite> writes, CancellationToken cancellationToken)
    {
        var held = writes.Select(w => (Write: w, Keys: KeysOf(w.Entry))).Where(static w => w.Keys.Count > 0).ToList();
        if (held.Count == 0)
        {
            return [];
        }

        var key = KeyOf(context, held[0].Write.Entry);
        var lockedParents = new Dictionary<object, TParent>();
        foreach (var keys in held.SelectMany(static w => w.Keys).Distinct().Chunk(PerQuery))
        {
            foreach (var parent in await Locked(context, key, keys, cancellationToken).ConfigureAwait(false))
            {
                lockedParents[key.GetGetter().GetClrValue(parent)!] = parent;
            }
        }

        var failures = new List<FieldFailure>();
        var said = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (write, keys) in held)
        {
            foreach (var parentKey in keys)
            {
                if (!lockedParents.TryGetValue(parentKey, out var parent))
                {
                    continue;
                }

                var failure = Failure(write, message(parent));
                if (said.Add(failure.Message + '\u001f' + string.Join('\u001f', failure.Fields)))
                {
                    failures.Add(failure);
                }
            }
        }

        return failures;
    }

    /// <summary>The parents of <paramref name="keys" />, past every query filter: what the check reads.</summary>
    internal static IQueryable<TParent> Touched(DbContext context, IProperty key, IReadOnlyList<object> keys)
    {
        var row = Expression.Parameter(typeof(TParent), "p");
        var stored = StoredProperty.Of(row, key);
        return context.Set<TParent>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(Expression.Lambda<Func<TParent, bool>>(AnyOf(stored, key.ClrType, keys, 0, keys.Count), row));
    }

    /// <summary>
    ///     The read that takes the update lock on the parents of <paramref name="keys" />: a bare <c>SELECT 1</c>, so the
    ///     parents' table is the first a rewrite finds (<see cref="ForUpdateSql" />).
    /// </summary>
    internal static IQueryable<int> ForUpdate(DbContext context, IProperty key, IReadOnlyList<object> keys) =>
        Touched(context, key, keys).TagWith(ForUpdateSql.Tag).Select(static p => 1);

    // Under an update lock first, every touched parent whether locked or not — the one a concurrent close would write.
    private async Task<List<TParent>> Locked(DbContext context, IProperty key, IReadOnlyList<object> keys, CancellationToken cancellationToken)
    {
        if (forUpdate)
        {
            await SaveTransaction.Open(context, cancellationToken).ConfigureAwait(false);
            await ForUpdate(context, key, keys).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        return await Touched(context, key, keys).Where(locked).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    // The parent the row was read with and the one it holds now: a move out of a locked parent is a write to it too. A
    // new row has none before; a key the store has not handed out yet is no stored parent's.
    private List<object> KeysOf(EntityEntry entry)
    {
        if (entry.Metadata.FindProperty(Property) is null)
        {
            throw new InvalidOperationException(
                $"{entry.Metadata.ClrType.Name}.LockedBy names '{Property}' as the property holding its parent, but it is " +
                "not a stored property: name the one holding the parent's key, as in n => n.PeriodId.");
        }

        var member = entry.Property(Property);
        var keys = new List<object>(2);
        if (entry.State != EntityState.Deleted && !member.IsTemporary && member.CurrentValue is { } current)
        {
            keys.Add(current);
        }

        if (entry.State != EntityState.Added && member.OriginalValue is { } original && !keys.Contains(original))
        {
            keys.Add(original);
        }

        return keys;
    }

    // Under the parent field when the save writes the row's parent; about the row (or the whole form, for a root) otherwise.
    private FieldFailure Failure(LockedWrite write, string text)
    {
        var entry = write.Entry;
        var writesParent = entry.State == EntityState.Added ||
                           (entry.State == EntityState.Modified && entry.Property(Property).IsModified);
        if (writesParent)
        {
            return new FieldFailure(text, [write.Path + Property]);
        }

        return new FieldFailure(text, write.Path.Length == 0 ? [] : [write.Path.TrimEnd('.')]);
    }

    private IProperty KeyOf(DbContext context, EntityEntry entry)
    {
        var parentType = context.Model.FindEntityType(typeof(TParent)) ?? throw new InvalidOperationException(
            $"{entry.Metadata.ClrType.Name}.LockedBy names {typeof(TParent).Name} as its parent, which this context does not map.");
        var held = entry.Metadata.FindProperty(Property)!.ClrType;
        return parentType.FindPrimaryKey()?.Properties is [var key] && key.ClrType == (Nullable.GetUnderlyingType(held) ?? held)
            ? key
            : throw new InvalidOperationException(
                $"{entry.Metadata.ClrType.Name}.LockedBy holds its parent in '{Property}' ({held.Name}), which is not " +
                $"{typeof(TParent).Name}'s key: name the property that holds a {typeof(TParent).Name}'s single key.");
    }

    // key = @a OR key = @b …, split in halves so a long list makes a shallow tree.
    private static BinaryExpression AnyOf(Expression stored, Type type, IReadOnlyList<object> keys, int from, int count) =>
        count == 1
            ? Expression.Equal(stored, QueryParameter.Of(keys[from], type))
            : Expression.OrElse(
                AnyOf(stored, type, keys, from, count / 2),
                AnyOf(stored, type, keys, from + (count / 2), count - (count / 2)));
}
