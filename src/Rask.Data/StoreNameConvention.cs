using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     Names every table and column the model did not name itself — by Rask's rule (<c>delivery_notes</c>,
///     <c>pistol_id</c>) or the app's <c>TableNames</c> and <c>ColumnNames</c> — and holds the rule its constraints are
///     named by.
/// </summary>
/// <remarks>
///     <para>
///         It takes the place of EF Core's <see cref="ModelCleanupConvention" />, the first convention to run as the model
///         is finalized, and names the tables and columns right after it — so every convention that reads a name later
///         (EF Core's shared-table checks and unique-index filters, Rask's <c>ConstraintNames</c> and full-text tables)
///         reads the app's, whatever order <c>Conventions</c> stated them in. Added last, as every other convention
///         is, it would run after those, and they would have read EF Core's.
///     </para>
///     <para>
///         At convention strength: a name stated explicitly (<c>ToTable</c>, <c>HasColumnName</c>, <c>[Table]</c>,
///         <c>[Column]</c>) is left as it was.
///     </para>
/// </remarks>
internal sealed class StoreNameConvention(ProviderConventionSetBuilderDependencies dependencies) : ModelCleanupConvention(dependencies)
{
    // One per model being configured, however many of TableNames, ColumnNames and ConstraintNames it calls.
    private static readonly ConditionalWeakTable<ModelConfigurationBuilder, StoreNameConvention> Configured = new();

    private bool _namesConstraints;

    public Func<TableName, string>? Tables { get; set; }

    public Func<ColumnName, string>? Columns { get; set; }

    public Func<Constraint, string?>? Constraints { get; set; }

    /// <summary>
    ///     Whether the constraints are named once every other convention is in place (a Rask context's), rather than where
    ///     <c>ConstraintNames</c> is called (a plain <c>DbContext</c>'s).
    /// </summary>
    public bool NamesConstraintsLast { get; private set; }

    /// <summary>
    ///     Rask's own names for <paramref name="configurationBuilder" />'s model, which the app's <c>TableNames</c>,
    ///     <c>ColumnNames</c> and <c>ConstraintNames</c> may replace; the constraints are named by
    ///     <see cref="NameConstraints" />, once every convention is in place.
    /// </summary>
    internal static void Defaults(ModelConfigurationBuilder configurationBuilder)
    {
        var naming = For(configurationBuilder);
        naming.Tables = static t => t.SnakeCasePlural;
        naming.Columns = static n => n.SnakeCase;
        naming.Constraints = static n => n.Prefixed;
        naming.NamesConstraintsLast = true;
    }

    /// <summary>
    ///     Adds the convention that names the model's constraints by <see cref="Constraints" />, read as the model is
    ///     finalized — once, however often it is asked.
    /// </summary>
    internal void NameConstraints(ModelConfigurationBuilder configurationBuilder)
    {
        if (_namesConstraints)
        {
            return;
        }

        _namesConstraints = true;
        configurationBuilder.Conventions.Add(services => new ConstraintNameConvention(
            this, string.Equals(services.GetService<IDatabaseProvider>()?.Name, ProviderNames.SqlServer, StringComparison.Ordinal)));
    }

    /// <summary>The convention naming <paramref name="configurationBuilder" />'s model, put in place on first use.</summary>
    internal static StoreNameConvention For(ModelConfigurationBuilder configurationBuilder) =>
        Configured.GetValue(configurationBuilder, static builder =>
        {
            StoreNameConvention? convention = null;
            builder.Conventions.Replace<ModelCleanupConvention>(services =>
                convention = new StoreNameConvention(services.GetRequiredService<ProviderConventionSetBuilderDependencies>()));
            return convention!;
        });

    public override void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        base.ProcessModelFinalizing(modelBuilder, context);

        var entityTypes = modelBuilder.Metadata.GetEntityTypes().ToList();

        if (Tables is { } tables)
        {
            entityTypes.Where(NamesItsTable).ToList().ForEach(e => NameTable(e, tables));
        }

        if (Columns is { } columns)
        {
            entityTypes.ForEach(e => NameColumns(e, columns));
        }
    }

    // A table of its own, not stated: not a type in its base's one table, not an owned type in its owner's, not a view.
    private static bool NamesItsTable(IConventionEntityType entityType) =>
        entityType.GetTableName() is not null &&
        !(entityType.FindOwnership() is { } ownership && SharesOwnersTable(ownership)) &&
        !IsInItsBasesTable(entityType) &&
        entityType.GetTableNameConfigurationSource() is not (ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation);

    private static bool IsInItsBasesTable(IConventionEntityType entityType) =>
        entityType.BaseType is not null &&
        entityType.GetRootType().GetMappingStrategy() is null or RelationalAnnotationNames.TphMappingStrategy;

    private static void NameTable(IConventionEntityType entityType, Func<TableName, string> name)
    {
        if (name(new TableName(entityType.ShortName(), entityType.GetTableName()!)) is { Length: > 0 } named)
        {
            entityType.Builder.ToTable(named, entityType.GetSchema());
        }
    }

    private static void NameColumns(IConventionEntityType entityType, Func<ColumnName, string> name)
    {
        // A type inside a JSON column has no columns of its own: the owner names the column.
        if (entityType.IsMappedToJson() ||
            (entityType.GetTableName() ?? entityType.GetViewName()) is not { } table)
        {
            return;
        }

        var store = entityType.GetTableName() is not null
            ? StoreObjectIdentifier.Table(table, entityType.GetSchema())
            : StoreObjectIdentifier.View(table, entityType.GetViewSchema());
        var prefix = PathInOwnersTable(entityType);
        var shared = entityType.FindOwnership() is { } ownership && SharesOwnersTable(ownership) ? ownership.Properties : [];

        foreach (var property in entityType.GetDeclaredProperties().Where(p => !shared.Contains(p)))
        {
            NameColumn(property, [.. prefix, property.Name], table, store, name);
        }

        foreach (var complex in entityType.GetDeclaredComplexProperties())
        {
            NameComplex(complex, [.. prefix, complex.Name], table, store, name);
        }

        foreach (var owned in entityType.GetDeclaredNavigations().Where(n => n.ForeignKey.IsOwnership && n.TargetEntityType.IsMappedToJson()))
        {
            NameContainer(owned.TargetEntityType, [.. prefix, owned.Name], table, name);
        }
    }

    private static void NameComplex(
        IConventionComplexProperty complex, string[] path, string table, StoreObjectIdentifier store, Func<ColumnName, string> name)
    {
        if (complex.ComplexType.IsMappedToJson())
        {
            NameContainer(complex.ComplexType, path, table, name);
            return;
        }

        foreach (var property in complex.ComplexType.GetDeclaredProperties())
        {
            NameColumn(property, [.. path, property.Name], table, store, name);
        }

        foreach (var nested in complex.ComplexType.GetDeclaredComplexProperties())
        {
            NameComplex(nested, [.. path, nested.Name], table, store, name);
        }
    }

    private static void NameColumn(
        IConventionProperty property, string[] path, string table, StoreObjectIdentifier store, Func<ColumnName, string> name)
    {
        if (property.GetColumnNameConfigurationSource() is ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation)
        {
            return;
        }

        var declared = property.GetColumnName(store) ?? property.GetColumnName();
        if (name(new ColumnName(table, path, declared)) is { Length: > 0 } named)
        {
            property.Builder.HasColumnName(named);
        }
    }

    private static void NameContainer(IConventionTypeBase json, string[] path, string table, Func<ColumnName, string> name)
    {
        // ToJson() states the column explicitly, under the property's own name: that one is EF Core's, and is named.
        if (json.GetContainerColumnName() is not { } declared ||
            (json.GetContainerColumnNameConfigurationSource() is ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation &&
             !string.Equals(declared, path[^1], StringComparison.Ordinal)))
        {
            return;
        }

        if (name(new ColumnName(table, path, declared)) is { Length: > 0 } named)
        {
            // The metadata setter: the builder would refuse to overwrite ToJson()'s explicit name.
            json.SetContainerColumnName(named);
        }
    }

    // An owned type in its owner's table is named after the navigation that holds it, as EF Core names it:
    // Address_Street, Shipping_Address_Street.
    private static string[] PathInOwnersTable(IConventionEntityType entityType) =>
        entityType.FindOwnership() is { } ownership && SharesOwnersTable(ownership)
            ? [.. PathInOwnersTable(ownership.PrincipalEntityType), ownership.PrincipalToDependent!.Name]
            : [];

    private static bool SharesOwnersTable(IConventionForeignKey ownership) =>
        string.Equals(ownership.DeclaringEntityType.GetTableName(), ownership.PrincipalEntityType.GetTableName(), StringComparison.Ordinal) &&
        string.Equals(ownership.DeclaringEntityType.GetSchema(), ownership.PrincipalEntityType.GetSchema(), StringComparison.Ordinal);
}
