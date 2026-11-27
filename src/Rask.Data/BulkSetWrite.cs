using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Rask.Cqrs;
using Rask.Values;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     <c>X.UpdateWhere</c> and <c>X.DeleteWhere</c>: one statement over every row a predicate matches, inside a
///     <c>Db.Transaction</c>, holding to what the aggregate declares about such a write.
/// </summary>
/// <remarks>
///     <para>
///         Each runs in a <c>Db.Transaction</c> of its own, or joins the one in progress: the declared locks
///         (<c>LockedBy</c>) are asked in the same transaction as the statement, the data-changed notices go out after
///         the commit, and the write counts once — as <c>Pairing (set)</c> — toward
///         <see cref="RaskDataOptions.OneAggregatePerTransaction" />.
///     </para>
///     <para>
///         The statement keeps what the save keeps: the query filters (tenant, soft delete), <c>UpdatedAt</c>,
///         <c>Version</c>, and a soft delete stamped rather than removed. What it cannot keep it refuses: a
///         <c>ValidateDelete()</c> asks each row in C#, so a type that declares one is deleted row by row instead.
///     </para>
/// </remarks>
internal static class BulkSetWrite
{
    internal static Task<int> Delete<[DynamicallyAccessedMembers(DataTrimming.Entity)] T>(
        Expression<Func<T, bool>> where, CancellationToken cancellationToken)
        where T : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(where);
        cancellationToken = Ambient.Or(cancellationToken);
        if (!ConventionRegistry.IsDeletable(typeof(T)))
        {
            throw new InvalidOperationException(
                $"{typeof(T).Name} says neither IDeletable nor ISoftDeletes, so {typeof(T).Name}.DeleteWhere has nothing it may do; " +
                "retire the rows through a method of their own (Cancel, Archive) instead.");
        }

        Tenant.DemandForWrite(typeof(T));
        return Db.Transaction(async () =>
        {
            var context = Db.CreateContext();
            await using var contextScope = context.ConfigureAwait(false);
            var type = context.Model.FindEntityType(typeof(T))!;
            RefuseRowByRowRules(type);

            var rows = context.Set<T>().Where(where);
            await RefuseLocked(context, type, rows, LockedWriteKind.Delete, touched: null, cancellationToken).ConfigureAwait(false);

            // A soft delete is a stamp, as SoftDeleteInterceptor makes it: DeletedAt alone, and the rows stay.
            var written = type.FindProperty(Columns.DeletedAt) is not null
                ? await rows.ExecuteUpdateAsync(
                    s => s.SetProperty(e => EF.Property<Timestamp?>(e, Columns.DeletedAt), Timestamp.Create(Now(context))),
                    cancellationToken).ConfigureAwait(false)
                : await rows.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            Written(context, type, written);
            return written;
        }, cancellationToken: cancellationToken);
    }

    internal static Task<int> Update<[DynamicallyAccessedMembers(DataTrimming.Entity)] T>(
        Expression<Func<T, bool>> where, Action<UpdateSettersBuilder<T>> set, CancellationToken cancellationToken)
        where T : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(where);
        ArgumentNullException.ThrowIfNull(set);
        cancellationToken = Ambient.Or(cancellationToken);
        Tenant.DemandForWrite(typeof(T));

        return Db.Transaction(async () =>
        {
            var context = Db.CreateContext();
            await using var contextScope = context.ConfigureAwait(false);
            var type = context.Model.FindEntityType(typeof(T))!;
            var rows = context.Set<T>().Where(where);

            // A row locked where it is now is refused before the statement; one the statement moved into a locked parent
            // after it, by the keys read first — still inside the transaction, so the refusal rolls the statement back.
            List<object>? keys = null;
            IReadOnlyCollection<string>? touched = null;
            if (!ParentLocks.Of(typeof(T)).IsEmpty)
            {
                touched = BulkSetters.Touched(set);
                await RefuseLocked(context, type, rows, LockedWriteKind.Change, touched, cancellationToken).ConfigureAwait(false);
                keys = await BulkKeys.Read(rows, BulkKeys.Of<T>(context, "UpdateWhere")).ToListAsync(cancellationToken).ConfigureAwait(false);
            }

            var now = Now(context);
            var written = await rows.ExecuteUpdateAsync(s =>
            {
                set(s);
                Stamp(s, type, now);
            }, cancellationToken).ConfigureAwait(false);

            if (keys is { Count: > 0 })
            {
                var key = type.FindPrimaryKey()!.Properties[0];
                foreach (var chunk in keys.Chunk(500))
                {
                    await RefuseLocked(context, type, ParentRead<T>.Touched(context, key, chunk), LockedWriteKind.Move, touched, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            Written(context, type, written);
            return written;
        }, cancellationToken: cancellationToken);
    }

    // What the save would stamp on a change: UpdatedAt, and the Version bumped, so a form read before it is refused.
    private static void Stamp<T>(UpdateSettersBuilder<T> setters, IEntityType type, DateTime now)
        where T : class
    {
        if (type.FindProperty(Columns.UpdatedAt) is { } updated)
        {
            if (updated.ClrType == typeof(Timestamp))
            {
                setters.SetProperty(e => EF.Property<Timestamp>(e, Columns.UpdatedAt), Timestamp.Create(now));
            }
            else if (updated.ClrType == typeof(DateTime))
            {
                setters.SetProperty(e => EF.Property<DateTime>(e, Columns.UpdatedAt), now);
            }
            else if (updated.ClrType == typeof(DateTime?))
            {
                setters.SetProperty(e => EF.Property<DateTime?>(e, Columns.UpdatedAt), now);
            }
        }

        if (typeof(IAggregate).IsAssignableFrom(type.ClrType) && type.FindProperty(Columns.Version) is { ClrType: var version } &&
            version == typeof(int))
        {
            setters.SetProperty(e => EF.Property<int>(e, Columns.Version), e => EF.Property<int>(e, Columns.Version) + 1);
        }
    }

    // A rule that asks each row in C# is no rule a statement can ask: the aggregate's own ValidateDelete(), or a child's
    // rule or lock, which a cascade would delete past.
    private static void RefuseRowByRowRules(IEntityType type)
    {
        foreach (var held in Held(type, 0))
        {
            if (WhyRowByRow(type, held) is not { } why)
            {
                continue;
            }

            var named = held == type ? type.ClrType.Name : $"{type.ClrType.Name}'s {held.ClrType.Name}";
            throw new InvalidOperationException(
                $"{type.ClrType.Name}.DeleteWhere cannot ask the rules of each row: {named} {why}, which a single DELETE " +
                $"statement would skip. Delete each one through {type.ClrType.Name}.Delete(id), which asks them.");
        }
    }

    private static string? WhyRowByRow(IEntityType type, IEntityType held)
    {
        if (GeneratedDomainRules.Of(held.ClrType)?.ValidateDelete is not null)
        {
            return "declares ValidateDelete()";
        }

        return held != type && !ParentLocks.Of(held.ClrType).IsEmpty ? "declares LockedBy" : null;
    }

    // The type and what it holds: its children, and theirs, as a cascade reaches them.
    private static IEnumerable<IEntityType> Held(IEntityType type, int depth)
    {
        yield return type;
        if (depth >= 4)
        {
            yield break;
        }

        foreach (var child in type.GetReferencingForeignKeys().Where(static k => k.IsOwnership || ModelBuilderExtensions.IsChildKey(k)))
        {
            foreach (var below in Held(child.DeclaringEntityType, depth + 1))
            {
                yield return below;
            }
        }
    }

    private static async Task RefuseLocked<T>(
        DbContext context, IEntityType type, IQueryable<T> rows, LockedWriteKind kind, IReadOnlyCollection<string>? touched,
        CancellationToken cancellationToken)
        where T : class
    {
        var failures = new List<FieldFailure>();
        foreach (var rule in ParentLocks.Of(typeof(T)))
        {
            if (await rule.CheckSet(context, type, rows, kind, touched, cancellationToken).ConfigureAwait(false) is { } failure)
            {
                failures.Add(failure);
            }
        }

        if (failures.Count > 0)
        {
            throw new RaskValidationException(failures);
        }
    }

    /// <summary>
    ///     What a save tells of a statement that wrote <paramref name="rows" /> of <paramref name="type" />: the policy counts
    ///     it as one write, and the pages showing them hear of it after the commit.
    /// </summary>
    internal static void Written(DbContext context, IEntityType type, int rows)
    {
        if (rows == 0)
        {
            return;
        }

        BulkInterceptors.Of<OneAggregatePerTransactionInterceptor>(context)?.CountSet(context, type);
        BulkInterceptors.Of<DataChangesInterceptor>(context)?.SetWritten(context, type);
    }

    // The clock AuditingInterceptor stamps with, so a frozen test clock is the same one either way.
    private static DateTime Now(DbContext context) => BulkInsertWriter.ResolveTimeProvider(context).GetUtcNow().UtcDateTime;
}
