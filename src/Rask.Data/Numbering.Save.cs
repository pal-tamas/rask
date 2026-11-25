using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

public sealed partial class Numbering<TEntity> : INumbering
{
    [DynamicallyAccessedMembers(DataTrimming.Entity)]
    Type INumbering.Entity => typeof(TEntity);

    async Task<NumberingTouch> INumbering.Touched(DbContext context, CancellationToken cancellationToken)
    {
        var touch = new NumberingTouch();
        var comparable = NumberingOrder.IsComparable(Order[0].ReturnType);
        var now = _now.Value;
        foreach (var entry in context.ChangeTracker.Entries<TEntity>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    PlaceNow(touch, entry, now, comparable);
                    break;
                case EntityState.Deleted:
                    PlaceWas(touch, entry, now, comparable);
                    break;
                case EntityState.Modified when entry.Members.Any(m => m.IsModified && _members.Contains(m.Metadata.Name)):
                    PlaceWas(touch, entry, now, comparable);
                    PlaceNow(touch, entry, now, comparable);
                    break;
            }
        }

        foreach (var key in FollowedRows(context))
        {
            if (await Spot(context, key, cancellationToken).ConfigureAwait(false) is { Within: { } group } spot)
            {
                touch.Touch(group, spot.First, comparable);
            }
        }

        return touch;
    }

    async Task INumbering.Renumber(DbContext context, NumberingTouch touched, CancellationToken cancellationToken)
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        var number = entity.FindProperty(NumberName)!;
        var key = entity.FindPrimaryKey()!.Properties[0];
        var numbered = new Dictionary<object, long?>();
        var changed = new Dictionary<object, long?>();
        await ReadBack(context, touched, cancellationToken).ConfigureAwait(false);
        foreach (var (group, since) in touched.Groups)
        {
            await RenumberGroup(context, number, key, group, since, numbered, changed, cancellationToken).ConfigureAwait(false);
        }

        await RenumberParts(context, number, key, touched, numbered, changed, cancellationToken).ConfigureAwait(false);
        foreach (var row in touched.Ungrouped)
        {
            await Write(context, number, key, row.Property(key.Name).CurrentValue!, null, row.Property(NumberName).CurrentValue, changed, cancellationToken)
                .ConfigureAwait(false);
        }

        Tell(context, number, key, changed);
    }

    private static TEntity Original(EntityEntry<TEntity> entry) => (TEntity)entry.OriginalValues.ToObject();

    private static IQueryable<TEntity> Where(IQueryable<TEntity> rows, ParameterExpression row, Expression? condition) =>
        condition is null ? rows : rows.Where(Expression.Lambda<Func<TEntity, bool>>(condition, row));

    private static BinaryExpression KeyIs(ParameterExpression row, IProperty key, object value) =>
        Expression.Equal(StoredProperty.Of(row, key), QueryValue.Of(value, key.ClrType));

    // The rows in memory say what the database now holds, and stay unchanged.
    private void Tell(DbContext context, IProperty number, IProperty key, Dictionary<object, long?> changed)
    {
        if (changed.Count == 0)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<TEntity>().ToList())
        {
            if (entry.State != EntityState.Detached && changed.TryGetValue(entry.Property(key.Name).CurrentValue!, out var value))
            {
                var property = entry.Property(NumberName);
                var stored = NumberValue.Write(number, value);
                property.CurrentValue = stored;
                property.OriginalValue = stored;
                property.IsModified = false;
            }
        }
    }

    // Where the row goes: its computed members computed from its values now, or — when C# cannot — read back once written.
    private void PlaceNow(NumberingTouch touch, EntityEntry<TEntity> entry, NumberingPlace<TEntity> now, bool comparable)
    {
        if (IsPart(entry.Entity))
        {
            touch.Parts.Add(entry);
        }
        else if (now.TryRead(entry.Entity, out var within, out var first))
        {
            PlaceAt(touch, entry, within, first, comparable, current: true);
        }
        else
        {
            touch.ReadBack.Add(entry);
        }
    }

    // Where the row was: as the database holds it, and as its values before the save compute it should they differ.
    private void PlaceWas(NumberingTouch touch, EntityEntry<TEntity> entry, NumberingPlace<TEntity> now, bool comparable)
    {
        var was = Original(entry);
        if (IsPart(was))
        {
            return;
        }

        PlaceAt(touch, entry, _readWithin(was), _readFirst(was), comparable, current: false);
        if (now.Computed && now.TryRead(was, out var within, out var first))
        {
            PlaceAt(touch, entry, within, first, comparable, current: false);
        }
    }

    private bool IsPart(TEntity row) => SameAs is not null && SameAs.Of(row) is not null;

    private static void PlaceAt(NumberingTouch touch, EntityEntry entry, object? within, object? first, bool comparable, bool current)
    {
        if (within is { } group)
        {
            touch.Touch(group, first, comparable);
        }
        else if (current)
        {
            touch.Ungrouped.Add(entry);
        }
    }

    // The rows placed only once the database computed their columns: placed by the values it read back, their groups
    // locked in the save's transaction before they are numbered.
    private async Task ReadBack(DbContext context, NumberingTouch touched, CancellationToken cancellationToken)
    {
        if (touched.ReadBack.Count == 0)
        {
            return;
        }

        var comparable = NumberingOrder.IsComparable(Order[0].ReturnType);
        var held = touched.LockNames(Name).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in touched.ReadBack)
        {
            var row = (TEntity)entry.Entity;
            PlaceAt(touched, entry, _readWithin(row), _readFirst(row), comparable, current: true);
        }

        touched.ReadBack.Clear();
        await NumberingLocks.Take(context, [.. touched.LockNames(Name).Where(name => !held.Contains(name))], cancellationToken)
            .ConfigureAwait(false);
    }

    // The numbered rows another table's written rows speak of, as they were and as they are.
    private List<object> FollowedRows(DbContext context)
    {
        var rows = new List<object>();
        foreach (var follower in _followers)
        {
            foreach (var entry in context.ChangeTracker.Entries().Where(e => e.Metadata.ClrType == follower.Other).ToList())
            {
                if (entry.State is EntityState.Added or EntityState.Modified && follower.RowOf(entry.Entity) is { } now)
                {
                    rows.Add(now);
                }

                if (entry.State is EntityState.Deleted or EntityState.Modified && follower.RowOf(entry.OriginalValues.ToObject()) is { } was)
                {
                    rows.Add(was);
                }
            }
        }

        return rows;
    }

    // Where a numbered row is counted: from memory when the context holds it, else from the database.
    private async Task<NumberedRow?> Spot(DbContext context, object key, CancellationToken cancellationToken)
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        var keyProperty = entity.FindPrimaryKey()!.Properties[0];
        if (context.ChangeTracker.Entries<TEntity>().FirstOrDefault(e => Equals(e.Property(keyProperty.Name).CurrentValue, key)) is { } tracked)
        {
            return _now.Value.TryRead(tracked.Entity, out var within, out var first)
                ? new NumberedRow { Within = within, First = first }
                : new NumberedRow { Within = _readWithin(tracked.Entity), First = _readFirst(tracked.Entity) };
        }

        var row = Expression.Parameter(typeof(TEntity), "row");
        var spot = Expression.Lambda<Func<TEntity, NumberedRow>>(
            Expression.MemberInit(
                Expression.New(typeof(NumberedRow)),
                Expression.Bind(typeof(NumberedRow).GetProperty(nameof(NumberedRow.Within))!, Boxed(Within, row)),
                Expression.Bind(typeof(NumberedRow).GetProperty(nameof(NumberedRow.First))!, Boxed(Order[0], row))),
            row);
        return await Where(context.Set<TEntity>(), row, KeyIs(row, keyProperty, key)).Select(spot)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    private static UnaryExpression Boxed(LambdaExpression lambda, ParameterExpression row) =>
        Expression.Convert(NumberingOrder.Rebind.To(lambda, row), typeof(object));

    private Expression Skipped(DbContext context, ParameterExpression row)
    {
        Expression skipped = Skip is null ? Expression.Constant(false) : NumberingOrder.Rebind.To(Skip, row);
        foreach (var follower in _followers)
        {
            skipped = Expression.OrElse(skipped, NumberingOrder.Rebind.To(follower.Skips(context), row));
        }

        return skipped;
    }

    // Not a part: a row that takes another's number has no place of its own.
    private BinaryExpression? Counted(ParameterExpression row) =>
        SameAs is null ? null : Expression.Equal(NumberingOrder.Rebind.To(SameAs.Row, row), Expression.Constant(null, SameAs.Row.ReturnType));

    private async Task RenumberGroup(
        DbContext context, IProperty number, IProperty key, object group, NumberingTouch.Since since,
        Dictionary<object, long?> numbered, Dictionary<object, long?> changed, CancellationToken cancellationToken)
    {
        var row = Expression.Parameter(typeof(TEntity), "row");
        Expression inGroup = Expression.Equal(NumberingOrder.Rebind.To(Within, row), QueryValue.Of(group, Within.ReturnType));
        if (Counted(row) is { } counted)
        {
            inGroup = Expression.AndAlso(inGroup, counted);
        }

        var rows = Where(context.Set<TEntity>(), row, inGroup);
        var next = 0L;
        if (!since.IsWhole)
        {
            var first = NumberingOrder.Rebind.To(Order[0], row);
            next = await LastBefore(rows, row, number, NumberingOrder.Compared(first, since.From!, atOrAfter: false), cancellationToken)
                .ConfigureAwait(false);
            rows = Where(rows, row, NumberingOrder.Compared(first, since.From!, atOrAfter: true));
        }

        var read = Expression.Lambda<Func<TEntity, NumberedRow>>(
            Expression.MemberInit(
                Expression.New(typeof(NumberedRow)),
                Expression.Bind(typeof(NumberedRow).GetProperty(nameof(NumberedRow.Key))!, Expression.Convert(StoredProperty.Of(row, key), typeof(object))),
                Expression.Bind(typeof(NumberedRow).GetProperty(nameof(NumberedRow.Number))!, Boxed(NumberLambda, row)),
                Expression.Bind(typeof(NumberedRow).GetProperty(nameof(NumberedRow.Skipped))!, Skipped(context, row))),
            row);
        var ordered = await NumberingOrder.Sorted(rows, row, Order, descending: false).Select(read).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var place in ordered)
        {
            long? at = place.Skipped ? null : ++next;
            numbered[place.Key!] = at;
            await Write(context, number, key, place.Key!, at, place.Number, changed, cancellationToken).ConfigureAwait(false);
        }
    }

    // The number of the last row counted before the place the numbering starts again: where its count goes on from.
    private async Task<long> LastBefore(
        IQueryable<TEntity> rows, ParameterExpression row, IProperty number, Expression before, CancellationToken cancellationToken)
    {
        var numberOf = NumberingOrder.Rebind.To(NumberLambda, row);
        var has = numberOf.Type.IsValueType && Nullable.GetUnderlyingType(numberOf.Type) is null
            ? before
            : Expression.AndAlso(before, Expression.NotEqual(numberOf, Expression.Constant(null, numberOf.Type)));
        var last = await NumberingOrder.Sorted(Where(rows, row, has), row, Order, descending: true)
            .Select(Expression.Lambda<Func<TEntity, object?>>(Expression.Convert(numberOf, typeof(object)), row))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return NumberValue.Read(number, last) ?? 0;
    }

    // The rows that take another's number: those of the rows numbered anew, and those the save wrote.
    private async Task RenumberParts(
        DbContext context, IProperty number, IProperty key, NumberingTouch touched,
        Dictionary<object, long?> numbered, Dictionary<object, long?> changed, CancellationToken cancellationToken)
    {
        if (SameAs is null)
        {
            return;
        }

        var wanted = numbered.Keys.Concat(touched.Parts.Select(p => SameAs.Of((TEntity)p.Entity)!)).Distinct().ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        foreach (var fill in wanted.Where(w => !numbered.ContainsKey(w)).ToList())
        {
            numbered[fill] = await NumberOf(context, number, key, fill, cancellationToken).ConfigureAwait(false);
        }

        var row = Expression.Parameter(typeof(TEntity), "row");
        var read = Expression.Lambda<Func<TEntity, NumberedRow>>(
            Expression.MemberInit(
                Expression.New(typeof(NumberedRow)),
                Expression.Bind(typeof(NumberedRow).GetProperty(nameof(NumberedRow.Key))!, Expression.Convert(StoredProperty.Of(row, key), typeof(object))),
                Expression.Bind(typeof(NumberedRow).GetProperty(nameof(NumberedRow.Number))!, Boxed(NumberLambda, row)),
                Expression.Bind(typeof(NumberedRow).GetProperty(nameof(NumberedRow.Same))!, Boxed(SameAs.Row, row))),
            row);
        var parts = await SameAs.Naming(context.Set<TEntity>(), wanted).Select(read).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var part in parts)
        {
            await Write(context, number, key, part.Key!, numbered.GetValueOrDefault(part.Same!), part.Number, changed, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<long?> NumberOf(DbContext context, IProperty number, IProperty key, object fill, CancellationToken cancellationToken)
    {
        var row = Expression.Parameter(typeof(TEntity), "row");
        var stored = await Where(context.Set<TEntity>(), row, KeyIs(row, key, fill))
            .Select(Expression.Lambda<Func<TEntity, object?>>(Boxed(NumberLambda, row), row))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return NumberValue.Read(number, stored);
    }

    private async Task Write(
        DbContext context, IProperty number, IProperty key, object rowKey, long? place, object? stored,
        Dictionary<object, long?> changed, CancellationToken cancellationToken)
    {
        if (NumberValue.Read(number, stored) == place)
        {
            return;
        }

        var row = Expression.Parameter(typeof(TEntity), "row");
        await _write(Where(context.Set<TEntity>(), row, KeyIs(row, key, rowKey)), NumberValue.Write(number, place), cancellationToken)
            .ConfigureAwait(false);
        changed[rowKey] = place;
    }
}
