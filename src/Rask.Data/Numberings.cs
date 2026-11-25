using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     Every numbering the models declared (<see cref="NumberedBuilderExtensions" />), recorded as each is built and kept
///     out of the model — an expression in an annotation would travel into the app's model snapshot — and the save step
///     that keeps them.
/// </summary>
internal static class Numberings
{
    private static readonly ConcurrentDictionary<string, INumbering> Declared = new(StringComparer.Ordinal);

    /// <summary>Records <paramref name="numbering" />, replacing what an earlier build of the model declared for it.</summary>
    internal static void Declare(INumbering numbering) => Declared[numbering.Entity.FullName + "." + numbering.Name] = numbering;

    /// <summary>
    ///     Notes what the save about to run touches of each numbering <paramref name="context" />'s model has, takes the
    ///     locks of the groups in the save's transaction, and leaves their numbering for once the rows went out.
    /// </summary>
    internal static async Task BeforeSave(DbContext context, CancellationToken cancellationToken)
    {
        if (Declared.IsEmpty)
        {
            return;
        }

        var touched = new List<(INumbering Numbering, NumberingTouch Touch)>();
        foreach (var numbering in In(context.Model))
        {
            var touch = await numbering.Touched(context, cancellationToken).ConfigureAwait(false);
            if (!touch.IsEmpty)
            {
                touched.Add((numbering, touch));
            }
        }

        if (touched.Count == 0)
        {
            return;
        }

        await SaveTransaction.Open(context, cancellationToken).ConfigureAwait(false);
        await NumberingLocks.Take(context, LockNames(touched), cancellationToken).ConfigureAwait(false);
        SaveFollowUps.Add(context, ct => AfterSave(context, touched, ct));
    }

    /// <summary>
    ///     Numbers <paramref name="groups" /> of every numbering <paramref name="entity" /> declares anew — every group when
    ///     null — in a <c>Db.Transaction</c> of its own or the one in progress (<see cref="Renumbering" />).
    /// </summary>
    internal static Task<int> Mend([DynamicallyAccessedMembers(DataTrimming.Entity)] Type entity, IReadOnlyCollection<object>? groups, CancellationToken cancellationToken)
    {
        cancellationToken = Ambient.Or(cancellationToken);
        Tenant.DemandForWrite(entity);
        return Db.Transaction(async () =>
        {
            var context = Db.CreateContext();
            await using var contextScope = context.ConfigureAwait(false);
            List<INumbering> declared = [.. In(context.Model).Where(n => n.Entity == entity)];
            if (declared.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{entity.Name} declares no b.Numbered(…) in its Configure, so there is nothing to renumber.");
            }

            var changed = 0;
            foreach (var numbering in declared)
            {
                changed += await numbering.Mend(context, groups, cancellationToken).ConfigureAwait(false);
            }

            BulkSetWrite.Written(context, context.Model.FindEntityType(entity)!, changed);
            return changed;
        }, cancellationToken: cancellationToken);
    }

    private static IEnumerable<INumbering> In(IModel model) =>
        Declared.Values.Where(n => model.FindEntityType(n.Entity) is { } entity && entity.FindProperty(n.NumberName) is not null);

    private static List<string> LockNames(List<(INumbering Numbering, NumberingTouch Touch)> touched) =>
        [.. touched.SelectMany(t => t.Touch.LockNames(t.Numbering.Name)).Distinct(StringComparer.Ordinal)];

    // In the save's transaction when it has one; a context that retries has none, and numbers in a transaction of its own.
    private static async Task AfterSave(
        DbContext context, List<(INumbering Numbering, NumberingTouch Touch)> touched, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            await RenumberAll(context, touched, cancellationToken).ConfigureAwait(false);
            return;
        }

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            var transaction = await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await NumberingLocks.Take(context, LockNames(touched), ct).ConfigureAwait(false);
                await RenumberAll(context, touched, ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RenumberAll(
        DbContext context, List<(INumbering Numbering, NumberingTouch Touch)> touched, CancellationToken cancellationToken)
    {
        foreach (var (numbering, touch) in touched)
        {
            await numbering.Renumber(context, touch, cancellationToken).ConfigureAwait(false);
        }
    }
}
