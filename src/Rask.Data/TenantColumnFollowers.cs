using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Data;

/// <summary>
///     A battery table that records a tenant without being partitioned by one — a stored file — keeps it in the
///     column type the application's own tenant-scoped tables use.
/// </summary>
/// <remarks>
///     <para>
///         The property stays a <c>Guid?</c>, the type <c>Current.Tenant</c> speaks. When every tenant-scoped table
///         in the model keeps a number (<c>IPerTenant&lt;int&gt;</c>, or a declared <c>int? TenantId</c>), the column
///         becomes that number, converted by the <see cref="TenantNumber" /> rule. Any other model — no tenant-scoped
///         table, <c>Guid?</c> ones, or tables of more than one type — keeps the <c>Guid?</c> column untouched.
///     </para>
///     <para>
///         Settled when the battery maps its table and again by <c>ApplyRaskConventions</c>, so the order of the two
///         calls in <c>OnModelCreating</c> does not matter. Registered in code rather than marked with an
///         annotation: an annotation would land in every app's migration snapshot.
///     </para>
/// </remarks>
internal static class TenantColumnFollowers
{
    private static readonly ConcurrentDictionary<Type, string> Followers = new();

    /// <summary>Makes <paramref name="property" /> of <paramref name="entity" /> follow the app's tenant type.</summary>
    internal static void Follow(ModelBuilder modelBuilder, Type entity, string property)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        Followers[entity] = property;
        Settle(modelBuilder);
    }

    /// <summary>Gives every following column in the model the type the app's tenant-scoped tables agree on.</summary>
    internal static void Settle(ModelBuilder modelBuilder)
    {
        if (Followers.IsEmpty)
        {
            return;
        }

        var column = AppColumnType(modelBuilder.Model);
        foreach (var (entity, name) in Followers)
        {
            if (modelBuilder.Model.FindEntityType(entity)?.FindProperty(name) is not { } property)
            {
                continue;
            }

            var converter = ConverterFor(column, entity.Name);

            // Only touched when there is something to change, so a Guid app's column carries nothing new.
            if (converter is not null || property.GetValueConverter() is not null)
            {
                property.SetValueConverter(converter);
            }
        }
    }

    /// <summary>The one column type every tenant-scoped table keeps its tenant in, or <c>Guid?</c>.</summary>
    internal static Type AppColumnType(IReadOnlyModel model)
    {
        var types = model.GetEntityTypes()
            .Where(static e => TenantParents.IsPartitioned(e.ClrType) && OwnsColumn(e))
            .Select(TenantColumn.TypeFor)
            .Distinct()
            .ToList();

        return types is [var only] ? only : typeof(Guid?);
    }

    // The tables that keep a tenant column of their own: a root, or a child that declares one.
    private static bool OwnsColumn(IReadOnlyEntityType entityType) =>
        typeof(IAggregate).IsAssignableFrom(entityType.ClrType) || entityType.FindProperty(Columns.TenantId) is not null;

    private static ValueConverter? ConverterFor(Type column, string table)
    {
        if (column == typeof(int?))
        {
            return new ValueConverter<Guid, int>(
                tenant => TenantColumn.Int32(tenant, table),
                number => TenantNumber.ToGuid(number));
        }

        return column == typeof(long?)
            ? new ValueConverter<Guid, long>(
                tenant => TenantColumn.Int64(tenant, table),
                number => TenantNumber.ToGuid(number))
            : null;
    }
}
