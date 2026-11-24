using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.Data;

/// <summary>
///     Keeps every JSON column on SQL Server <c>nvarchar(max)</c> — a value collection, a primitive collection, a complex or owned type mapped
///     with <c>ToJson()</c> — whatever compatibility level the context runs at.
/// </summary>
/// <remarks>
///     <para>
///         At compatibility level 170 EF Core maps a JSON column to SQL Server 2025's native <c>json</c> type, below it
///         to <c>nvarchar(max)</c>. Left to EF Core the model — and so every migration — would change with the level the
///         server reports, and a column created at 170 could not be created on a 150 or 160 server at all. Moving a
///         column to <c>json</c> is a migration of its own, not a side effect of the level.
///     </para>
///     <para>
///         Elsewhere the provider's default already holds: SQLite stores JSON as <c>TEXT</c>, and PostgreSQL as
///         <c>jsonb</c> (a primitive collection as an array), which a model made before this convention already says. A type the
///         app states itself (<c>SetContainerColumnType</c>, <c>HasColumnType</c>) wins: this writes at convention strength, and only where no
///         type is stated.
///     </para>
/// </remarks>
/// <param name="providerName">The EF Core provider the model is built for, or null when there is none.</param>
internal sealed class JsonContainerTextConvention(string? providerName) : IModelFinalizingConvention
{
    /// <summary>SQL Server's text of any length, which every compatibility level has.</summary>
    internal const string SqlServerText = "nvarchar(max)";

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        if (!string.Equals(providerName, ProviderNames.SqlServer, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            // An owned type mapped with ToJson() is the container itself; one nested inside it lives in its column.
            if (entityType.IsMappedToJson() && entityType.FindOwnership()?.PrincipalEntityType.IsMappedToJson() != true)
            {
                if (entityType.GetContainerColumnType() is null)
                {
                    entityType.SetContainerColumnType(SqlServerText);
                }

                continue;
            }

            if (!entityType.IsMappedToJson())
            {
                Type(entityType.GetDeclaredProperties(), entityType.GetDeclaredComplexProperties());
            }
        }
    }

    // A complex type mapped to JSON is a container; one stored in columns of its own may hold one further down, and
    // its primitive collections are JSON columns of their own.
    private static void Type(IEnumerable<IConventionProperty> properties, IEnumerable<IConventionComplexProperty> complexProperties)
    {
        foreach (var collection in properties.Where(static p => p.IsPrimitiveCollection && p.GetColumnType() is null))
        {
            collection.SetColumnType(SqlServerText);
        }

        foreach (var type in complexProperties.Select(static c => c.ComplexType))
        {
            if (!type.IsMappedToJson())
            {
                Type(type.GetDeclaredProperties(), type.GetDeclaredComplexProperties());
            }
            else if (type.GetContainerColumnType() is null)
            {
                type.SetContainerColumnType(SqlServerText);
            }
        }
    }
}
