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
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    IDatabaseProvider databaseProvider,
    ISqlGenerationHelper sqlGenerationHelper)
    : MigrationsModelDiffer(
        typeMappingSource,
        migrationsAnnotationProvider,
        relationalAnnotationProvider,
        rowIdentityMapFactory,
        commandBatchPreparerDependencies)
{
    private const string EmptyObject = "{}";
    private const string EmptyArray = "[]";
    private const string RegisteredKey = "Rask:MigrationsModelDiffer";

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
    public static void Register(IServiceCollection services)
    {
        services.AddScoped<IMigrationsModelDiffer, RaskMigrationsModelDiffer>();

        // Keyed by name, not by a type: this file is compiled into Rask.SqlServer too, whose copy of a marker type would
        // be another type to the convention in Rask.Data that asks for it.
        services.TryAddKeyedSingleton<object>(RegisteredKey, RegisteredKey);
    }

    /// <summary>Whether this differ makes the migrations of the context <paramref name="services" /> belong to.</summary>
    /// <param name="services">EF Core's internal service provider, as a convention is handed it.</param>
    public static bool IsRegistered(IServiceProvider services) =>
        services is IKeyedServiceProvider keyed && keyed.GetKeyedService<object>(RegisteredKey) is not null;

    /// <inheritdoc />
    public override IReadOnlyList<MigrationOperation> GetDifferences(IRelationalModel? source, IRelationalModel? target)
    {
        var operations = base.GetDifferences(source, target);
        if (target is null)
        {
            return operations;
        }

        operations = WithTenantReferences(operations, source, target);
        operations = WithGuidOrder(operations, source, target);
        operations = WithNoOverlapRules(operations, source, target);

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

    /// <inheritdoc />
    /// <remarks>
    ///     A model that starts or stops storing its ids in time order is a change a migration must make, though no column
    ///     changes: an app started on it without one stops at <c>HasPendingModelChanges</c> instead of misreading the rows.
    /// </remarks>
    public override bool HasDifferences(IRelationalModel? source, IRelationalModel? target) =>
        base.HasDifferences(source, target) ||
        (source is not null && target is not null && IsOrderedByTime(source) != IsOrderedByTime(target) &&
         string.Equals(databaseProvider.Name, ProviderNames.SqlServer, StringComparison.Ordinal));

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

    // The one-time rewrite of the rows a database holds when the model starts (or stops) storing its uniqueidentifier
    // columns in time order. Turning it on, it runs after the schema has changed, so a column renamed, or retyped from
    // text, by the same migration is rewritten under its new name — and before the seed data, which the model writes in
    // the stored form already; tables the migration creates are empty. Turning it off, it runs first, on the columns as
    // the database still has them.
    private IReadOnlyList<MigrationOperation> WithGuidOrder(
        IReadOnlyList<MigrationOperation> operations, IRelationalModel? source, IRelationalModel target)
    {
        var before = IsOrderedByTime(source);
        var after = IsOrderedByTime(target);
        if (source is null || before == after ||
            !string.Equals(databaseProvider.Name, ProviderNames.SqlServer, StringComparison.Ordinal))
        {
            return operations;
        }

        if (!after)
        {
            return GuidOrderRewrite.Of(source, new HashSet<(string Name, string? Schema)>(), SqlServerGuidOrder.NativeFromStored, sqlGenerationHelper) is { } back
                ? [back, .. operations]
                : operations;
        }

        var created = operations.OfType<CreateTableOperation>().Select(static c => (c.Name, c.Schema)).ToHashSet();
        if (GuidOrderRewrite.Of(target, created, SqlServerGuidOrder.StoredFromNative, sqlGenerationHelper) is not { } rewrite)
        {
            return operations;
        }

        var seeds = operations.ToList().FindIndex(static o => o is InsertDataOperation or UpdateDataOperation or DeleteDataOperation);
        return seeds < 0 ? [.. operations, rewrite] : [.. operations.Take(seeds), rewrite, .. operations.Skip(seeds)];
    }

    private static bool IsOrderedByTime(IRelationalModel? model) =>
        string.Equals(model?.Model.FindAnnotation(SqlServerGuidOrder.Annotation)?.Value as string, SqlServerGuidOrder.ByTime, StringComparison.Ordinal);

    // PostgreSQL's exclusion constraints and SQL Server's triggers for b.NoOverlap(…), which no EF Core operation
    // describes: dropped first where the rule changed, went away, or its table's columns change under it (SQL Server
    // cannot alter a column its index holds), and created last, once every column is there. SQLite's triggers are its
    // migrations generator's (INoOverlapEnforcer): its table rebuilds take them with them.
    private List<MigrationOperation> WithNoOverlapRules(
        IReadOnlyList<MigrationOperation> operations, IRelationalModel? source, IRelationalModel target)
    {
        var provider = databaseProvider.Name;
        if (provider is not (ProviderNames.Postgres or ProviderNames.SqlServer))
        {
            return [.. operations];
        }

        var limit = target.Model.GetMaxIdentifierLength();
        var before = NoOverlapRules(source, provider, limit);
        var after = NoOverlapRules(target, provider, limit);
        if (before.Count == 0 && after.Count == 0)
        {
            return [.. operations];
        }

        var dropped = operations.OfType<DropTableOperation>().Select(static d => (d.Name, d.Schema)).ToHashSet();
        var reshaped = operations.Select(static o => o switch
            {
                AlterColumnOperation a => (a.Table, a.Schema),
                DropColumnOperation d => (d.Table, d.Schema),
                RenameColumnOperation r => (r.Table, r.Schema),
                _ => ((string, string?)?)null,
            })
            .OfType<(string, string?)>()
            .ToHashSet();

        var drops = before
            .Where(b => !dropped.Contains(b.Table) && (reshaped.Contains(b.Table) || !after.Contains(b)))
            .SelectMany(b => NoOverlapSql.Drop(provider, b.Rule, b.Table.Name, b.Table.Schema, limit))
            .Select(static sql => new SqlOperation { Sql = sql });
        var creates = after
            .Where(a => reshaped.Contains(a.Table) || !before.Contains(a))
            .SelectMany(static a => a.Create)
            .Distinct(StringComparer.Ordinal)
            .Select(static sql => new SqlOperation { Sql = sql });

        return [.. drops, .. operations, .. creates];
    }

    private static List<NoOverlapAt> NoOverlapRules(IRelationalModel? model, string provider, int limit) =>
        model is null
            ? []
            : [.. model.Tables.SelectMany(table => table.EntityTypeMappings
                .Select(static m => m.TypeBase).OfType<IEntityType>()
                .SelectMany(NoOverlapRule.Of)
                .Select(rule => new NoOverlapAt(
                    (table.Name, table.Schema),
                    rule,
                    [.. NoOverlapSql.Create(provider, rule, table, [.. table.PrimaryKey?.Columns.Select(static c => c.Name) ?? []], limit)])))];

    // A rule on its table, equal to another when it would be created by the same statements.
    private sealed record NoOverlapAt((string Name, string? Schema) Table, NoOverlapRule Rule, IReadOnlyList<string> Create)
    {
        public bool Equals(NoOverlapAt? other) =>
            other is not null && Table == other.Table && Create.SequenceEqual(other.Create, StringComparer.Ordinal);

        public override int GetHashCode() => HashCode.Combine(Table, string.Concat(Create));
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
