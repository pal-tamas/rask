using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>The keys of the rows a query matches, read before a write that loads each one (<c>UpdateEach</c>, <c>Upsert</c>).</summary>
internal static class BulkKeys
{
    /// <summary>The single key property of <typeparamref name="T" />, as <paramref name="context" /> maps it.</summary>
    /// <exception cref="InvalidOperationException">The aggregate's key has more than one part.</exception>
    internal static IProperty Of<[DynamicallyAccessedMembers(DataTrimming.Entity)] T>(DbContext context, string write)
    {
        var type = context.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException(
            $"'{typeof(T).Name}' is not mapped by the configured context, so {typeof(T).Name}.{write} has no table to write.");

        return type.FindPrimaryKey()?.Properties is [var key]
            ? key
            : throw new InvalidOperationException(
                $"{typeof(T).Name}.{write} loads each row by its key, and {typeof(T).Name}'s key has several parts. " +
                $"Load and save them one by one through {typeof(T).Name}.Find(…) and Save().");
    }

    /// <summary>The keys of <paramref name="rows" />, in key order, untracked.</summary>
    internal static IQueryable<object> Read<T>(IQueryable<T> rows, IProperty key)
        where T : class
    {
        var row = Expression.Parameter(typeof(T), "r");
        var stored = Expression.Lambda<Func<T, object>>(Expression.Convert(StoredProperty.Of(row, key), typeof(object)), row);
        return rows.AsNoTracking().OrderBy(stored).Select(stored);
    }
}
