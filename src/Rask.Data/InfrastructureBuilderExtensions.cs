using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     <c>b.Infrastructure()</c> — a table that is plumbing beside the domain, not an aggregate of it: never counted
///     against <see cref="RaskDataOptions.OneAggregatePerTransaction" />.
/// </summary>
public static class InfrastructureBuilderExtensions
{
    private static readonly ConcurrentDictionary<Type, bool> Marked = new();

    /// <summary>
    ///     Marks the entity a table of infrastructure — a login session, a queue, a cache — whose rows a transaction writes
    ///     beside its aggregate without making it a second one. Rask's own tables (outbox, jobs, mail, sessions, stored
    ///     files, cache, push subscribers) are marked already.
    /// </summary>
    /// <typeparam name="TEntity">The entity.</typeparam>
    /// <param name="entity">Its builder, in its <c>Configure</c> or <c>OnModelCreating</c>.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     A fact about the type, kept beside the model rather than in it, so marking a table changes no migration.
    /// </remarks>
    public static EntityTypeBuilder<TEntity> Infrastructure<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        this EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        Marked[typeof(TEntity)] = true;
        return entity;
    }

    /// <summary>Whether <paramref name="type" /> was marked <see cref="Infrastructure{TEntity}" />.</summary>
    /// <remarks>A table of Rask's own batteries is, as they record it for its names (<see cref="KeptNames" />).</remarks>
    internal static bool IsInfrastructure(Type type) => Marked.ContainsKey(type) || KeptNames.Has(type);
}
