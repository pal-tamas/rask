using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.EntityFrameworkCore.Update.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

// MigrationsModelDiffer and its dependencies are EF Core's "internal" API, and the only place that decides the default
// of a column a migration turns NOT NULL. ValueCollectionNullabilityTests pin the result on every provider, so an EF
// Core upgrade that moves it goes red instead of writing "{}" again.
#pragma warning disable EF1001

/// <summary>
///     EF Core's migration differ, with an empty ARRAY — not an object — as the default of a JSON collection column that
///     a migration adds or turns <c>NOT NULL</c>, and the foreign keys from a tenant's tables to the tenant (<c>IBelongsTo&lt;Tenant&gt;</c>).
/// </summary>
/// <remarks>
///     <para>
///     EF Core gives every JSON column that becomes <c>NOT NULL</c> the default <c>"{}"</c>, which every provider then
///     writes into the rows that are there: right for one value object, but a collection of them can read none of those
///     rows back. <c>"[]"</c> is what an empty collection is stored as, so the generated migration fills them with that.
///     A JSON column holding ONE value object keeps <c>"{}"</c>.
///     </para>
///     <para>
///         A <see cref="TenantReference" /> is no foreign key of the model, so EF Core's differ never sees it: it is added
///         here from the annotations of the two models — inside the <c>CREATE TABLE</c> on SQLite, which makes a foreign
///         key only with its table, and after every other operation elsewhere, once the tenant's table is there too.
///     </para>
/// </remarks>
internal sealed class RaskMigrationsModelDiffer(
    IRelationalTypeMappingSource typeMappingSource,
    IMigrationsAnnotationProvider migrationsAnnotationProvider,
    IRelationalAnnotationProvider relationalAnnotationProvider,
    IRowIdentityMapFactory rowIdentityMapFactory,
    CommandBatchPreparerDependencies commandBatchPreparerDependencies,
    IDatabaseProvider databaseProvider)
    : MigrationsModelDiffer(
        typeMappingSource,
        migrationsAnnotationProvider,
        relationalAnnotationProvider,
        rowIdentityMapFactory,
        commandBatchPreparerDependencies)
{
    private const string EmptyObject = "{}";
    private const string EmptyArray = "[]";

    /// <summary>
    ///     Registers this differ in a context's internal services, from the <c>ApplyServices</c> of an options extension
    ///     the context already carries.
    /// </summary>
    /// <param name="services">EF Core's internal service collection.</param>
    /// <remarks>
    ///     Not <c>ReplaceService</c>: that changes the options' identity, so every context that has it gets an internal
    ///     service provider of its own, and EF Core refuses a process that builds more than twenty. An extension the
    ///     context carries anyway adds none. Added, not tried: the provider may register EF Core's own differ first, and
    ///     the last registration wins.
    /// </remarks>
    public static void Register(IServiceCollection services) =>
        services.AddScoped<IMigrationsModelDiffer, RaskMigrationsModelDiffer>();

    /// <inheritdoc />
    public override IReadOnlyList<MigrationOperation> GetDifferences(IRelationalModel? source, IRelationalModel? target)
    {
        var operations = base.GetDifferences(source, target);
        if (target is null)
        {
            return operations;
        }

        operations = WithTenantReferences(operations, source, target);

        foreach (var operation in operations)
        {
            switch (operation)
            {
                case AddColumnOperation { DefaultValue: EmptyObject } added when IsJsonCollection(target, added.Table, added.Schema, added.Name):
                    added.DefaultValue = EmptyArray;
                    break;
                case AlterColumnOperation { DefaultValue: EmptyObject } altered when IsJsonCollection(target, altered.Table, altered.Schema, altered.Name):
                    altered.DefaultValue = EmptyArray;
                    break;
            }
        }

        return operations;
    }

    // The tenant references the target declares and the source did not, added; the ones it no longer declares, dropped.
    private List<MigrationOperation> WithTenantReferences(
        IReadOnlyList<MigrationOperation> operations, IRelationalModel? source, IRelationalModel target)
    {
        var before = TenantReference.In(source);
        var after = TenantReference.In(target);
        var sqlite = string.Equals(databaseProvider.Name, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal);
        var created = operations.OfType<CreateTableOperation>().ToDictionary(static c => (c.Name, c.Schema));
        var dropped = operations.OfType<DropTableOperation>().Select(static d => (d.Name, d.Schema)).ToHashSet();
        var drops = new List<MigrationOperation>();
        var adds = new List<MigrationOperation>();

        foreach (var (table, was) in before)
        {
            if (!dropped.Contains(table) && !created.ContainsKey(table)
                && (!after.TryGetValue(table, out var now) || now != was))
            {
                drops.Add(new DropForeignKeyOperation { Name = was.Reference.Name, Table = table.Table, Schema = table.Schema });
            }
        }

        foreach (var (table, now) in after)
        {
            var key = ForeignKey(table.Table, table.Schema, now.Column, now.Reference);
            if (created.TryGetValue(table, out var create) && sqlite)
            {
                create.ForeignKeys.Add(key);
            }
            else if (!sqlite && (created.ContainsKey(table) || !before.TryGetValue(table, out var was) || was != now))
            {
                adds.Add(key);
            }
        }

        return [.. drops, .. operations, .. adds];
    }

    private static AddForeignKeyOperation ForeignKey(string table, string? schema, string column, TenantReference reference) =>
        new()
        {
            Name = reference.Name,
            Table = table,
            Schema = schema,
            Columns = [column],
            PrincipalTable = reference.PrincipalTable,
            PrincipalSchema = reference.PrincipalSchema,
            PrincipalColumns = [reference.PrincipalColumn],
            OnDelete = ReferentialAction.NoAction,
            OnUpdate = ReferentialAction.NoAction,
        };

    // Whether the column holds a collection of value objects as JSON: a complex collection whose container it is, on
    // an entity the table maps or inside one of its complex properties.
    private static bool IsJsonCollection(IRelationalModel model, string table, string? schema, string column) =>
        model.FindTable(table, schema)?.EntityTypeMappings.Any(m => m.TypeBase is IEntityType entity &&
            HoldsJsonCollection(entity, column)) == true;

    private static bool HoldsJsonCollection(ITypeBase type, string column) =>
        type.GetComplexProperties().Any(complex =>
            complex.ComplexType.IsMappedToJson()
                ? complex.IsCollection && string.Equals(complex.ComplexType.GetContainerColumnName(), column, StringComparison.Ordinal)
                : HoldsJsonCollection(complex.ComplexType, column));
}
#pragma warning restore EF1001
