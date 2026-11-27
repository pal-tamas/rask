using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     The relationship each <see cref="BelongsTo{TAggregate}" /> stands for — the generated foreign key to the other
///     aggregate's key, with no navigation on either side and no cascade — as generated code declared it.
/// </summary>
/// <remarks>
///     <para>
///         Mapped as <c>b.HasOne&lt;Vehicle&gt;().WithMany().HasForeignKey("VehicleId")</c> is, which is what makes a move
///         from that hand-written pair to a <c>BelongsTo</c> no migration: the entity's own <c>Configure</c> saying the same
///         relationship is the same relationship, and its <c>OnDelete</c> wins.
///     </para>
///     <para>
///         The delete is <see cref="DeleteBehavior.NoAction" /> below an explicit one: an aggregate is its own boundary, so
///         deleting it never reaches into the rows that point at it — not in the database, which refuses the delete, and
///         not in a context tracking both, which EF Core's <c>ClientSetNull</c> would write. In the database it is the
///         constraint EF Core gives an optional foreign key by default, so a hand-written one moves with no migration. An app-wide <c>DeleteBehavior(…)</c> rule, which runs
///         as the model is finalized, and the entity's <c>Configure</c> can still say otherwise.
///     </para>
///     <para>
///         A context that maps the entity but not the aggregate it names keeps the id as a plain column.
///     </para>
/// </remarks>
internal static class BelongsToRelationships
{
    private static readonly ConcurrentDictionary<Type, ConcurrentQueue<Action<ModelBuilder>>> Declared = new();

    /// <summary>Records the relationship of one <c>BelongsTo</c>, by its entity and the aggregate it names.</summary>
    /// <typeparam name="TEntity">The entity declaring the reference.</typeparam>
    /// <typeparam name="TAggregate">The aggregate referred to.</typeparam>
    /// <param name="foreignKey">The generated id — <c>VehicleId</c>.</param>
    public static void Declare<
        [DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity,
        [DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(string foreignKey)
        where TEntity : class
        where TAggregate : class =>
        Declared.GetOrAdd(typeof(TEntity), static _ => new ConcurrentQueue<Action<ModelBuilder>>())
            .Enqueue(modelBuilder => Map<TEntity, TAggregate>(modelBuilder, foreignKey));

    /// <summary>Maps the relationship of every <c>BelongsTo</c> of the entities <paramref name="entityTypes" /> lists.</summary>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="entityTypes">The entity types the model holds, before the conventions added any.</param>
    public static void Apply(ModelBuilder modelBuilder, IEnumerable<IMutableEntityType> entityTypes)
    {
        foreach (var entityType in entityTypes)
        {
            if (Declared.TryGetValue(entityType.ClrType, out var relationships))
            {
                foreach (var map in relationships)
                {
                    map(modelBuilder);
                }
            }
        }
    }

    private static void Map<
        [DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity,
        [DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(ModelBuilder modelBuilder, string foreignKey)
        where TEntity : class
        where TAggregate : class
    {
        if (modelBuilder.Model.FindEntityType(typeof(TAggregate)) is null)
        {
            return;
        }

        var key = (IConventionForeignKey)modelBuilder.Entity<TEntity>()
            .HasOne<TAggregate>()
            .WithMany()
            .HasForeignKey(foreignKey)
            .Metadata;

        key.SetDeleteBehavior(DeleteBehavior.NoAction, fromDataAnnotation: true);
    }
}
