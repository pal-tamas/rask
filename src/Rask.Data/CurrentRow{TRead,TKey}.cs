using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     A <see cref="CurrentRow" /> read as <typeparamref name="TRead" />, the read face of an aggregate keyed by
///     <typeparamref name="TKey" />.
/// </summary>
/// <remarks>
///     The key is the aggregate's own type, and the framework carries it as a <see cref="Guid" />: a
///     <see cref="Guid" /> is itself, a <see cref="string" /> is that <see cref="Guid" /> written out
///     (<c>D</c> format), and an <see cref="int" /> or a <see cref="long" /> is the number the <see cref="Guid" />
///     carries (<see cref="TenantNumber" />). The build allows no other key, so the last arm is never reached.
/// </remarks>
internal sealed class CurrentRow<[DynamicallyAccessedMembers(DataTrimming.Entity)] TRead, TKey>(
    CurrentRowKind kind,
    Type aggregate,
    Func<TKey, Expression<Func<TRead, bool>>> byKey,
    Func<TRead, TKey> keyOf) : CurrentRow(kind, aggregate)
    where TRead : class, IReadModel
{
    /// <inheritdoc />
    protected override object? Load(Guid key)
    {
        using var context = ReadDb.CreateContext();

        // Synchronous all the way down — EF's own sync path over the provider's sync ADO.NET — because the caller is a
        // property getter. Never a task waited on: that is a deadlock on a single-threaded host and a starved pool
        // on a busy one.
        return context.Set<TRead>().AsNoTracking().FirstOrDefault(byKey(KeyOf(key)));
    }

    /// <inheritdoc />
    protected override async Task<object?> LoadAsync(Guid key, CancellationToken cancellationToken)
    {
        var context = ReadDb.CreateContext();
        await using (context.ConfigureAwait(false))
        {
            return await context.Set<TRead>().AsNoTracking()
                .FirstOrDefaultAsync(byKey(KeyOf(key)), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override bool Owns(object row, Guid key)
    {
        var held = keyOf((TRead)row);

        // A text key compares as the Guid it holds: a column that keeps it in capitals is still the same tenant.
        return typeof(TKey) == typeof(string)
            ? Guid.TryParse((string?)(object?)held, out var parsed) && parsed == key
            : EqualityComparer<TKey>.Default.Equals(held, KeyOf(key));
    }

    private TKey KeyOf(Guid key)
    {
        if (typeof(TKey) == typeof(Guid))
        {
            return (TKey)(object)key;
        }

        if (typeof(TKey) == typeof(string))
        {
            return (TKey)(object)key.ToString("D");
        }

        if (typeof(TKey) == typeof(long))
        {
            return (TKey)(object)Number(key);
        }

        if (typeof(TKey) == typeof(int))
        {
            var number = Number(key);
            return number is >= int.MinValue and <= int.MaxValue
                ? (TKey)(object)(int)number
                : throw NotTheKey(key);
        }

        throw new NotSupportedException(
            $"{Aggregate.Name} is keyed by {typeof(TKey).Name}; the current {Kind.ToString().ToLowerInvariant()} " +
            "is a Guid, a string, an int or a long.");
    }

    private long Number(Guid key) =>
        TenantNumber.TryRead(key, out var number) ? number : throw NotTheKey(key);

    private InvalidOperationException NotTheKey(Guid key) =>
        new($"{Aggregate.Name} is keyed by {typeof(TKey).Name}, and the current " +
            $"{Kind.ToString().ToLowerInvariant()} {key} is not one: it is an identifier in its own right, not a " +
            "number carried as one. Open the work with the number itself — Tenant.Use(42).");
}
