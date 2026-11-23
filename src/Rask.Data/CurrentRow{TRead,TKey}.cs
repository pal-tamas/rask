using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     A <see cref="CurrentRow" /> read as <typeparamref name="TRead" />, the read face of an aggregate keyed by
///     <typeparamref name="TKey" />.
/// </summary>
/// <remarks>
///     <para>
///         The key is the aggregate's own type, and the framework carries it as a <see cref="Guid" />
///         (<see cref="CurrentKey" />).
///     </para>
///     <para>
///         The user's row is read across tenants: it is looked up by the signed-in user's own key, which names one
///         row whatever tenant the work is in — an administrator working in another tenant is still themselves, and
///         a user aggregate that is <c>PerTenant</c> would otherwise read as gone there.
///     </para>
/// </remarks>
internal sealed class CurrentRow<[DynamicallyAccessedMembers(DataTrimming.Entity)] TRead, TKey>(
    CurrentRowKind kind,
    Type aggregate,
    Func<TKey, Expression<Func<TRead, bool>>> byKey,
    Func<TRead, TKey> keyOf) : CurrentRow(kind, aggregate)
    where TRead : class, IReadModel
{
    /// <inheritdoc />
    internal override bool IsNumbered => CurrentKey.IsNumber<TKey>();

    /// <inheritdoc />
    protected override object? Load(Guid key)
    {
        using var across = AcrossForTheUser();
        using var context = ReadDb.CreateContext();

        // Synchronous all the way down — EF's own sync path over the provider's sync ADO.NET — because the caller is a
        // property getter. Never a task waited on: that is a deadlock on a single-threaded host and a starved pool
        // on a busy one.
        return context.Set<TRead>().AsNoTracking().FirstOrDefault(byKey(KeyOf(key)));
    }

    /// <inheritdoc />
    protected override async Task<object?> LoadAsync(Guid key, CancellationToken cancellationToken)
    {
        using var across = AcrossForTheUser();
        using var loading = Loading.Read();
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

    private IDisposable? AcrossForTheUser() => Kind is CurrentRowKind.User ? Tenant.Across() : null;

    private TKey KeyOf(Guid key) => CurrentKey.Of<TKey>(key, Kind, Aggregate.Name);
}
