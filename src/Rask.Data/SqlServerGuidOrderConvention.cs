using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     On SQL Server, stores every <c>uniqueidentifier</c> column in the form <see cref="SqlServerGuidOrder" /> sorts by
///     time — so <c>ORDER BY Id</c>, keyset paging and <c>Min</c>/<c>Max</c> answer as on PostgreSQL and SQLite, and a
///     version-7 key is appended to its clustered index instead of scattered through it.
/// </summary>
/// <remarks>
///     <para>
///         Every <see cref="Guid" />, <see cref="Guid" />? and typed id over a <see cref="Guid" /> whose column is a
///         <c>uniqueidentifier</c>: a join compares the stored bytes, so a key and the foreign keys to it must all be in
///         one layout. A converter the property has already — a typed id's, an app's that ends in a <see cref="Guid" /> —
///         runs first; a column of text (<c>nvarchar</c>, a <c>HasConversion&lt;string&gt;()</c>) or JSON is left as it is.
///         No default and no value generation is added: Rask assigns every key in the application.
///     </para>
///     <para>
///         Only where Rask's migration differ is in force too (<c>UseRaskSqlServer</c>, a <c>RaskApp</c>): the model then
///         carries <see cref="SqlServerGuidOrder.Annotation" />, and the migration that first sees it rewrites the rows an
///         existing database holds. Without that differ a database made before would be misread, so nothing changes.
///     </para>
/// </remarks>
/// <param name="typeMappings">The provider's type mappings, or null where the convention is off.</param>
internal sealed class SqlServerGuidOrderConvention(IRelationalTypeMappingSource? typeMappings) : IModelFinalizingConvention
{
    private const string StoreType = "uniqueidentifier";

    /// <summary>The convention for the provider <paramref name="services" /> belong to: off anywhere but SQL Server.</summary>
    internal static SqlServerGuidOrderConvention For(IServiceProvider services) =>
        new(string.Equals(services.GetService<IDatabaseProvider>()?.Name, ProviderNames.SqlServer, StringComparison.Ordinal)
            && RaskMigrationsModelDiffer.IsRegistered(services)
                ? services.GetService<IRelationalTypeMappingSource>()
                : null);

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        // A read model follows the write model it mirrors: rewriting a face's id the write side stores as it is would
        // compare it with bytes no row holds.
        if (typeMappings is null || modelBuilder.Metadata.FindAnnotation(SqlServerGuidOrder.WriteSideAnnotation)?.Value is false)
        {
            return;
        }

        modelBuilder.HasAnnotation(SqlServerGuidOrder.Annotation, SqlServerGuidOrder.ByTime);
        // An owned type kept as JSON holds its ids as text inside the document.
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().Where(static e => !e.IsMappedToJson()))
        {
            Store(entityType);
        }
    }

    private void Store(IConventionTypeBase type)
    {
        foreach (var property in type.GetDeclaredProperties().Where(IsUniqueIdentifier))
        {
            Store(property);
        }

        foreach (var complex in type.GetDeclaredComplexProperties().Where(static c => !c.ComplexType.IsMappedToJson()))
        {
            Store(complex.ComplexType);
        }
    }

    private void Store(IConventionProperty property)
    {
        // Who decided how the key is generated, and what: a converter must change neither.
        var generated = property.ValueGenerated;
        var generatedBy = property.GetValueGeneratedConfigurationSource();

        property.SetValueConverter(StoredGuidConverter.After(property.GetValueConverter()));
        property.Builder.HasTypeMapping(typeMappings!.FindMapping((IProperty)property));

        if (property.ValueGenerated != generated)
        {
            property.SetValueGenerated(generatedBy is null ? null : generated, generatedBy == ConfigurationSource.Explicit);
        }
    }

    // A Guid on its way to a uniqueidentifier column, and not in the stored form yet (a read face mirrors its write
    // model's converter, which already is).
    private bool IsUniqueIdentifier(IConventionProperty property)
    {
        var converter = property.GetValueConverter();
        if (StoredGuidConverter.IsIn(converter) || property.GetProviderClrType() is { } provider && provider != typeof(Guid))
        {
            return false;
        }

        var stored = converter?.ProviderClrType ?? Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (stored != typeof(Guid))
        {
            return false;
        }

        var storeType = property.GetColumnType() ?? ((property.FindTypeMapping() as RelationalTypeMapping) ?? typeMappings!.FindMapping((IProperty)property))?.StoreType;
        return string.Equals(storeType, StoreType, StringComparison.OrdinalIgnoreCase);
    }
}
