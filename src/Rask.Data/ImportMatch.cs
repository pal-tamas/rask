using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>The natural key an import matches its rows by (<see cref="ImportOptions{T, TRow}.Upsert{TKey}" />).</summary>
/// <param name="update">Changes the stored aggregate from the row.</param>
internal abstract class ImportMatch<[DynamicallyAccessedMembers(DataTrimming.Entity)] T, TRow>(Action<T, TRow> update)
    where T : class
{
    /// <summary>Changes the stored aggregate from the row.</summary>
    internal Action<T, TRow> Update { get; } = update;

    /// <summary>The key of an aggregate the factory built.</summary>
    internal abstract object KeyOf(T aggregate);

    /// <summary>The primary key of each stored aggregate whose natural key is among <paramref name="built" />'s, by natural key.</summary>
    internal abstract Task<Dictionary<object, object>> Stored(DbContext context, IReadOnlyList<T> built, CancellationToken cancellationToken);
}

/// <summary>A natural key of <typeparamref name="TKey" />.</summary>
/// <param name="key">The property that names one aggregate.</param>
/// <param name="update">Changes the stored aggregate from the row.</param>
internal sealed class ImportMatch<[DynamicallyAccessedMembers(DataTrimming.Entity)] T, TRow, TKey>(Expression<Func<T, TKey>> key, Action<T, TRow> update) : ImportMatch<T, TRow>(update)
    where T : class
    where TKey : notnull
{
    // List<TKey>.Contains, which EF Core translates to IN.
    private static readonly MethodInfo Contains =
        ((MethodCallExpression)((Expression<Func<List<TKey>, TKey, bool>>)((keys, one) => keys.Contains(one))).Body).Method;

    private readonly Func<T, TKey> _read = key.Compile();

    internal override object KeyOf(T aggregate) => _read(aggregate);

    internal override async Task<Dictionary<object, object>> Stored(
        DbContext context, IReadOnlyList<T> built, CancellationToken cancellationToken)
    {
        var keys = built.Select(_read).Distinct().ToList();
        var stored = new Dictionary<object, object>();
        if (keys.Count == 0)
        {
            return stored;
        }

        var among = Expression.Lambda<Func<T, bool>>(Expression.Call(Expression.Constant(keys), Contains, key.Body), key.Parameters[0]);
        var primary = context.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties[0];
        var found = await context.Set<T>().AsNoTracking().Where(among).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var aggregate in found)
        {
            stored[_read(aggregate)] = primary.GetGetter().GetClrValue(aggregate)!;
        }

        return stored;
    }
}
