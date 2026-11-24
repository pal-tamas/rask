using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     Makes each <see cref="OneInForce{TChild}" /> a unique index on the child's key to its aggregate, over the rows the
///     child's <c>InForceWhile(…)</c> keeps — written as the provider's own filter by the filtered-index convention.
/// </summary>
/// <remarks>
///     Run after every entity's own <c>Configure</c>, where the condition is declared and where the relationship may
///     have been mapped by hand.
/// </remarks>
internal static class InForceIndexes
{
    /// <summary>Declares every aggregate's index; refuses one whose child says nothing about when it is in force.</summary>
    /// <exception cref="InvalidOperationException">A child declares no condition, or is not held by its aggregate.</exception>
    internal static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var parent in modelBuilder.Model.GetEntityTypes().ToList())
        {
            foreach (var childType in ConventionRegistry.InForceChildrenOf(parent.ClrType))
            {
                Declare(modelBuilder, parent, childType);
            }
        }

        // The declaration is a lambda, which no migration snapshot can write: it has done its work once the index has it.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            entityType.RemoveAnnotation(InForceBuilderExtensions.Annotation);
        }
    }

    private static void Declare(ModelBuilder modelBuilder, IMutableEntityType parent, Type childType)
    {
        var child = modelBuilder.Model.FindEntityType(childType) ?? throw new InvalidOperationException(
            $"'{parent.ClrType.Name}' keeps one '{childType.Name}' in force, but '{childType.Name}' is not in the model. " +
            $"Hold it in a collection of '{parent.ClrType.Name}'.");

        if (child.FindAnnotation(InForceBuilderExtensions.Annotation)?.Value is not InForceDeclaration declared)
        {
            throw new InvalidOperationException(
                $"'{parent.ClrType.Name}' keeps one '{childType.Name}' in force, but '{childType.Name}' does not say when " +
                $"one is. Declare it in its Configure: b.InForceWhile(x => x.EndedAt == null).");
        }

        var key = child.GetForeignKeys().FirstOrDefault(k =>
                      ReferenceEquals(k.PrincipalEntityType, parent) && k.PrincipalToDependent is { IsCollection: true })
                  ?? throw new InvalidOperationException(
                      $"'{parent.ClrType.Name}' keeps one '{childType.Name}' in force, but holds no collection of them. " +
                      $"Hold them on it — IReadOnlyList<{childType.Name}> or Children<{childType.Name}, …>.");

        // Named, so it stands beside the foreign key's own index rather than replacing it with one over some rows only.
        var index = modelBuilder.Entity(child.ClrType).HasIndex(
            [.. key.Properties.Select(static p => p.Name)], $"IX_{child.GetTableName() ?? childType.Name}_InForce");
        index.IsUnique(declared.Message);
        index.Metadata.SetAnnotation(FilteredIndexConvention.Annotation, declared.Rows);
    }
}
