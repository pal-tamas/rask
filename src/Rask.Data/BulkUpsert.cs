using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     <c>X.Upsert</c>: the one aggregate a predicate matches changed through its own method, or a new one built by its
///     factory when none does — saved, its rules asked, in one transaction.
/// </summary>
internal static class BulkUpsert
{
    internal static Task<Upserted<T>> Run<[DynamicallyAccessedMembers(DataTrimming.Entity)] T>(
        Expression<Func<T, bool>> match, Func<Task<T>> create, Func<T, Task> change, CancellationToken cancellationToken)
        where T : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(change);
        cancellationToken = Ambient.Or(cancellationToken);
        Tenant.DemandForWrite(typeof(T));

        return Db.Transaction(async () =>
        {
            List<object> found;
            var context = Db.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                var key = BulkKeys.Of<T>(context, "Upsert");
                found = await BulkKeys.Read(context.Set<T>().Where(match), key).Take(2).ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (found.Count > 1)
            {
                throw new InvalidOperationException(
                    $"{typeof(T).Name}.Upsert matched more than one {typeof(T).Name}, so it cannot tell which to change: match " +
                    $"on what makes one unique, and back it with a unique index (b.IsUnique(…)).");
            }

            if (found is [var existing])
            {
                var aggregate = await GeneratedModelWrites.Find<T>(existing, cancellationToken).ConfigureAwait(false)
                                ?? throw GeneratedModelWrites.Missing<T>(existing);
                await change(aggregate).ConfigureAwait(false);
                await GeneratedModelWrites.Save(aggregate, cancellationToken: cancellationToken).ConfigureAwait(false);
                return new Upserted<T>(aggregate, Created: false);
            }

            var made = await create().ConfigureAwait(false);
            await GeneratedModelWrites.Save(made, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new Upserted<T>(made, Created: true);
        }, cancellationToken: cancellationToken);
    }
}
