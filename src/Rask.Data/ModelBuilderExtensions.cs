using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
/// Applies Rask.Data's model conventions in a single call from <c>OnModelCreating</c>.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// For every mapped entity: adds a global query filter (<c>DeletedAt == null</c>) to each
    /// <see cref="ISoftDeletable"/> and marks each <see cref="IVersioned"/>'s <c>Version</c> as the
    /// optimistic-concurrency token. Call after the entity type configurations are applied (they establish
    /// the entity types this walks).
    /// </summary>
    public static ModelBuilder ApplyRaskConventions(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).HasQueryFilter(BuildNotDeletedFilter(clrType));
            }

            if (typeof(IVersioned).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType)
                    .Property(nameof(IVersioned.Version))
                    .IsConcurrencyToken();
            }
        }

        return modelBuilder;
    }

    // Builds `e => e.DeletedAt == null` for the given entity CLR type (EF's non-generic HasQueryFilter
    // takes a LambdaExpression, so we synthesize the typed lambda here).
    private static LambdaExpression BuildNotDeletedFilter(Type clrType)
    {
        var parameter = Expression.Parameter(clrType, "e");
        var deletedAt = Expression.Property(parameter, nameof(ISoftDeletable.DeletedAt));
        var body = Expression.Equal(deletedAt, Expression.Constant(null, typeof(DateTime?)));
        return Expression.Lambda(body, parameter);
    }
}
