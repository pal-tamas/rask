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
        DbContext context, IReadOnlyList<LockTarget> writes, CancellationToken cancellationToken);
}

/// <summary>A lock on a <typeparamref name="T" /> whose parent is a <typeparamref name="TParent" />.</summary>
/// <typeparam name="T">The row that is locked.</typeparam>
/// <typeparam name="TParent">The parent.</typeparam>
internal sealed class ParentLock<T, [DynamicallyAccessedMembers(DataTrimming.Entity)] TParent>(
    string property,
    Expression<Func<TParent, bool>> locked,
    Func<TParent, LockedWrite<T>, string> message,
    LockRead read) : ParentLock(property)
    where T : class
    where TParent : class
{
    // Parents read per query: one parameter each, well inside SQL Server's 2100.
    private const int PerQuery = 500;

    private readonly List<Func<IQueryable<TParent>, IQueryable<TParent>>> _includes = [];

    /// <summary>The row's properties a change may touch alone, under a locked parent too (<see cref="LockedByBuilder{T,TParent}.Except" />).</summary>
    internal HashSet<string> Excepted { get; } = new(StringComparer.Ordinal);

    /// <summary>Loads a navigation of each locked parent, for the message to read.</summary>
    internal void Include(Func<IQueryable<TParent>, IQueryable<TParent>> include) => _includes.Add(include);

    internal override async Task<IReadOnlyList<FieldFailure>> Check(
        DbContext context, IReadOnlyList<LockTarget> writes, CancellationToken cancellationToken)
    {
        var held = writes
            .Where(w => !OnlyExcepted(w.Entry))
            .Select(w => (Write: w, Keys: KeysOf(w.Entry)))
            .Where(static w => w.Keys.Count > 0)
            .ToList();
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

                var failure = Failure(write, parent);
                if (said.Add(failure.Message + '\u001f' + string.Join('\u001f', failure.Fields)))
                {
                    failures.Add(failure);
                }
            }
        }

        return failures;
    }

    // Under an update lock first, every touched parent whether locked or not — the one a concurrent close would write.
    private async Task<List<TParent>> Locked(DbContext context, IProperty key, IReadOnlyList<object> keys, CancellationToken cancellationToken)
    {
        if (read == LockRead.ForUpdate)
        {
            await SaveTransaction.Open(context, cancellationToken).ConfigureAwait(false);
            await ParentRead<TParent>.ForUpdate(context, key, keys).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        var query = ParentRead<TParent>.Touched(context, key, keys).Where(locked);
        foreach (var include in _includes)
        {
            query = include(query);
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    // A change to nothing but the excepted columns passes; an add, a delete, or a change through a child never does.
    private bool OnlyExcepted(EntityEntry entry) =>
        Excepted.Count > 0 &&
        entry.State == EntityState.Modified &&
        entry.Properties.Where(static p => p.IsModified).Select(static p => p.Metadata.Name)
            .Concat(entry.ComplexProperties.Where(static p => p.IsModified).Select(static p => p.Metadata.Name))
            .All(Excepted.Contains);

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
    private FieldFailure Failure(LockTarget write, TParent parent)
    {
        var kind = KindOf(write.Entry);
        var text = message(parent, new LockedWrite<T>(kind, (T)write.Entry.Entity));
        if (kind is LockedWriteKind.Add or LockedWriteKind.Move)
        {
            return new FieldFailure(text, [write.Path + Property]);
        }

        return new FieldFailure(text, write.Path.Length == 0 ? [] : [write.Path.TrimEnd('.')]);
    }

    private LockedWriteKind KindOf(EntityEntry entry) => entry.State switch
    {
        EntityState.Added => LockedWriteKind.Add,
        EntityState.Deleted => LockedWriteKind.Delete,
        EntityState.Modified when entry.Property(Property).IsModified => LockedWriteKind.Move,
        _ => LockedWriteKind.Change,
    };

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
}
