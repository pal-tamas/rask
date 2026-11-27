using Microsoft.EntityFrameworkCore;
using Rask.Values;

namespace Rask.Data;

/// <summary>
///     An entity's stamps read and written through instants, for a battery that names no <c>Rask.Core</c> type —
///     Rask.Storage (#1086): a <see cref="Timestamp" /> is Core's, and a member that takes or returns one would put the
///     reference into the battery's assembly.
/// </summary>
internal static class Stamps
{
    /// <summary>The rows whose <c>CreatedAt</c> is before <paramref name="instant" />.</summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="rows">The rows.</param>
    /// <param name="instant">The instant.</param>
    /// <returns>The rows created before it.</returns>
    internal static IQueryable<T> CreatedBefore<T>(IQueryable<T> rows, DateTimeOffset instant)
        where T : class, IEntity
    {
        var before = Timestamp.At(instant);
        return rows.Where(e => EF.Property<Timestamp>(e, Columns.CreatedAt) < before);
    }

    /// <summary>When <paramref name="entity" /> was created, as the instant its <c>CreatedAt</c> holds.</summary>
    /// <typeparam name="TId">The entity's key.</typeparam>
    /// <param name="entity">The entity.</param>
    /// <returns>The instant.</returns>
    internal static DateTimeOffset CreatedOf<TId>(Entity<TId> entity) => entity.CreatedAt.Instant;
}
