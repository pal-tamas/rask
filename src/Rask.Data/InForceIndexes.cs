using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     Makes each child's <c>InForceWhile(…)</c> a unique index on its key to the aggregate that holds it, over the rows
///     the condition keeps — written as the provider's own filter by the filtered-index convention.
/// </summary>
/// <remarks>
///     Run after every entity's own <c>Configure</c>, where the condition is declared and where the relationship may
///     have been mapped by hand.
/// </remarks>
internal static class InForceIndexes
{
    /// <summary>Declares the index of every child that says when one is in force.</summary>
    /// <exception cref="InvalidOperationException">A child that says it is held in no aggregate's collection.</exception>
    internal static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var child in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (child.FindAnnotation(InForceBuilderExtensions.Annotation)?.Value is InForceDeclaration declared)
            {
                Declare(modelBuilder, child, declared);
            }
        }

        // The declaration is a lambda, which no migration snapshot can write: it has done its work once the index has it.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            entityType.RemoveAnnotation(InForceBuilderExtensions.Annotation);
        }
    }

    // The aggregate is the one holding the child in a collection: of the rows it holds, one is in force.
    private static void Declare(ModelBuilder modelBuilder, IMutableEntityType child, InForceDeclaration declared)
    {
        var childType = child.ClrType;
        var key = child.GetForeignKeys().FirstOrDefault(static k =>
                      k.PrincipalToDependent is { IsCollection: true } && typeof(IAggregate).IsAssignableFrom(k.PrincipalEntityType.ClrType))
                  ?? throw new InvalidOperationException(
                      $"'{childType.Name}' says when one of it is in force, but no aggregate holds a collection of them. " +
                      $"Hold them on the aggregate — IReadOnlyList<{childType.Name}> or Children<{childType.Name}, …>.");

        // Named, so it stands beside the foreign key's own index rather than replacing it with one over some rows only.
        var index = modelBuilder.Entity(childType).HasIndex(
            [.. key.Properties.Select(static p => p.Name)], $"IX_{child.GetTableName() ?? childType.Name}_InForce");
        index.IsUnique(declared.Message);
        index.Metadata.SetAnnotation(FilteredIndexConvention.Annotation, declared.Rows);
    }
}
