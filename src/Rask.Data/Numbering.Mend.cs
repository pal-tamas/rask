using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>A numbering asked to number groups anew by hand (<see cref="Renumbering" />): rows a save never numbered.</summary>
public sealed partial class Numbering<TEntity>
{
    async Task<int> INumbering.Mend(DbContext context, IReadOnlyCollection<object>? groups, CancellationToken cancellationToken)
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        var number = entity.FindProperty(NumberName)!;
        var key = entity.FindPrimaryKey()!.Properties[0];
        var touched = new NumberingTouch();
        foreach (var group in groups ?? await Groups(context, cancellationToken).ConfigureAwait(false))
        {
            touched.Touch(Grouped(group), first: null, comparable: false);
        }

        // The same locks a save takes, so a save into one of these groups waits for it, and it for the save.
        await NumberingLocks.Take(context, touched.LockNames(Name), cancellationToken).ConfigureAwait(false);
        var numbered = new Dictionary<object, long?>();
        var changed = new Dictionary<object, long?>();
        foreach (var (group, since) in touched.Groups)
        {
            await RenumberGroup(context, number, key, group, since, numbered, changed, cancellationToken).ConfigureAwait(false);
        }

        await RenumberParts(context, number, key, touched, numbered, changed, cancellationToken).ConfigureAwait(false);
        if (groups is null)
        {
            await Unnumber(context, number, key, changed, cancellationToken).ConfigureAwait(false);
        }

        return changed.Count;
    }

    // Every group a row of the table is counted within, as the database holds them.
    private async Task<IReadOnlyCollection<object>> Groups(DbContext context, CancellationToken cancellationToken)
    {
        var row = Expression.Parameter(typeof(TEntity), "row");
        var groups = await context.Set<TEntity>().Select(Expression.Lambda<Func<TEntity, object?>>(Boxed(Within, row), row)).Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. groups.OfType<object>()];
    }

    // A group as the caller named it, refused when it is not of the type the rows are counted within.
    private object Grouped(object group)
    {
        var type = Nullable.GetUnderlyingType(Within.ReturnType) ?? Within.ReturnType;
        return type.IsInstanceOfType(group)
            ? group
            : throw new ArgumentException(
                $"{Name} is counted within a {type.Name} ({Within}), and the group asked for is a {group.GetType().Name}.", nameof(group));
    }

    // The rows in no group keep no number: those numbered before they left every group, past a save.
    private async Task Unnumber(DbContext context, IProperty number, IProperty key, Dictionary<object, long?> changed, CancellationToken cancellationToken)
    {
        if (Within.ReturnType.IsValueType && Nullable.GetUnderlyingType(Within.ReturnType) is null)
        {
            return;
        }

        var row = Expression.Parameter(typeof(TEntity), "row");
        var numberOf = NumberingOrder.Rebind.To(NumberLambda, row);
        Expression ungrouped = Expression.Equal(NumberingOrder.Rebind.To(Within, row), Expression.Constant(null, Within.ReturnType));
        if (!numberOf.Type.IsValueType || Nullable.GetUnderlyingType(numberOf.Type) is not null)
        {
            ungrouped = Expression.AndAlso(ungrouped, Expression.NotEqual(numberOf, Expression.Constant(null, numberOf.Type)));
        }

        if (Counted(row) is { } counted)
        {
            ungrouped = Expression.AndAlso(ungrouped, counted);
        }

        var read = Expression.Lambda<Func<TEntity, NumberedRow>>(
            Expression.MemberInit(
                Expression.New(typeof(NumberedRow)),
                Expression.Bind(typeof(NumberedRow).GetProperty(nameof(NumberedRow.Key))!, Expression.Convert(StoredProperty.Of(row, key), typeof(object))),
                Expression.Bind(typeof(NumberedRow).GetProperty(nameof(NumberedRow.Number))!, Boxed(NumberLambda, row))),
            row);
        var rows = await Where(context.Set<TEntity>(), row, ungrouped).Select(read).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var place in rows)
        {
            await Write(context, number, key, place.Key!, null, place.Number, changed, cancellationToken).ConfigureAwait(false);
        }
    }
}
