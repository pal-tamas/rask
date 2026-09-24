using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
/// The options extension <c>UseRaskSqlite</c> adds for full-text search: it registers, in EF Core's internal service
/// provider, the convention mapping each index as a queryable entity, the <c>highlight</c>/<c>snippet</c>
/// translator, and the interceptor that rewrites <c>Search</c>.
/// </summary>
/// <remarks>
/// Registering them as services rather than through <c>AddInterceptors</c> is what makes a second
/// <c>UseRaskSqlite</c> call harmless: EF keeps one extension per type, so the rewrite runs once however many times
/// the context is configured.
/// </remarks>
internal sealed class FullTextSearchOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    // EF Core's services builder activates both plugins by reflection without a trimming annotation, so their
    // constructors are kept here; the interceptor's ServiceDescriptor is annotated and needs nothing.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(FullTextSearchConventionSetPlugin))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(FullTextFunctionTranslatorPlugin))]
    public void ApplyServices(IServiceCollection services)
    {
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IConventionSetPlugin, FullTextSearchConventionSetPlugin>()
            .TryAdd<IMethodCallTranslatorPlugin, FullTextFunctionTranslatorPlugin>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInterceptor, FullTextSearchQueryInterceptor>());
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Rask SQLite full-text search ";

        // Every instance registers the same thing, so they can share an internal service provider.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:SqliteFullTextSearch"] = "1";
    }
}

/// <summary>Adds <see cref="FullTextSearchEntityConvention"/> to the model's conventions.</summary>
internal sealed class FullTextSearchConventionSetPlugin : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.Add(new FullTextSearchEntityConvention());
        return conventionSet;
    }
}

/// <summary>
/// Maps each declared full-text index — and, for a table without an <c>INTEGER</c> key, its key map — as a keyless
/// property-bag entity, so a query can join to it like to any table.
/// </summary>
/// <remarks>
/// <para>
/// Both are excluded from migrations: <see cref="FullTextSearchDdl"/> creates them, because EF Core has no way to
/// express a virtual table. They exist in the model only so the query pipeline can name their columns.
/// </para>
/// <para>
/// FTS5 exposes three columns a query needs that are not in the declaration: <c>rowid</c>; a hidden column named
/// after the table itself, where <c>index = 'words'</c> is a full-text match; and <c>rank</c>, the bm25 score (lower
/// is better). The row id is typed as the entity's own key where it IS the key, because comparing an <c>int</c> key
/// to a <c>long</c> makes EF Core emit a <c>CAST</c> that stops SQLite seeking the primary key.
/// </para>
/// </remarks>
internal sealed class FullTextSearchEntityConvention : IModelFinalizingConvention
{
    public const string RowId = "RowId";
    public const string Match = "Match";
    public const string Rank = "Rank";

    public static string IndexEntityName(IReadOnlyEntityType entityType) => $"{entityType.Name}.FullTextIndex";

    public static string KeyEntityName(IReadOnlyEntityType entityType) => $"{entityType.Name}.FullTextKeys";

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            if (entityType.FindAnnotation(FullTextSearchSpec.AnnotationName) is null
                || entityType.GetTableName() is not { } table
                || entityType.FindPrimaryKey() is not { } key)
            {
                continue;
            }

            var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());
            var usesRowid = FullTextSearchDdl.UsesRowid(entityType);

            // Recorded, so it travels into the migration's saved model and the DDL builds the layout queries expect.
            entityType.SetAnnotation(
                FullTextSearchDdl.LayoutAnnotation,
                usesRowid ? FullTextSearchDdl.RowidLayout : FullTextSearchDdl.KeyMapLayout);

            var index = Bag(modelBuilder, IndexEntityName(entityType), FullTextSearchDdl.IndexTable(table), entityType.GetSchema());
            if (usesRowid)
            {
                KeyColumn(index, key.Properties[0], RowId, "rowid", store);
            }
            else
            {
                Column(index, typeof(long), RowId, "rowid");
            }

            Column(index, typeof(string), Match, FullTextSearchDdl.IndexTable(table));
            Column(index, typeof(double), Rank, "rank");

            if (!usesRowid)
            {
                var keys = Bag(modelBuilder, KeyEntityName(entityType), FullTextSearchDdl.KeyTable(table), entityType.GetSchema());
                Column(keys, typeof(long), RowId, "rowid");

                foreach (var property in key.Properties)
                {
                    KeyColumn(keys, property, property.Name, property.GetColumnName(store) ?? property.Name, store);
                }
            }
        }
    }

    private static IConventionEntityTypeBuilder Bag(
        IConventionModelBuilder modelBuilder,
        string name,
        string table,
        string? schema)
    {
        var builder = modelBuilder.SharedTypeEntity(name, typeof(Dictionary<string, object>))
            ?? throw new InvalidOperationException($"The full-text index entity '{name}' could not be added to the model.");

        builder.HasNoKey();
        builder.ToTable(table, schema);
        builder.Metadata.SetIsTableExcludedFromMigrations(true);
        return builder;
    }

    // A column holding the searched entity's key: same CLR type, same conversion, same store type, so comparing the two
    // needs no CAST — and a strongly-typed id still reads back as itself.
    private static void KeyColumn(
        IConventionEntityTypeBuilder entity,
        IConventionProperty key,
        string name,
        string column,
        StoreObjectIdentifier store)
    {
        var property = Column(entity, key.ClrType, name, column);

        if (key.GetValueConverter() is { } converter)
        {
            property.HasConversion(converter);
        }
        else if (FullTextSearchDdl.ValueConverterTypeOf(key) is { } converterType)
        {
            property.HasConverter(converterType);
        }

        if (key.GetColumnType(store) is { } columnType)
        {
            property.HasColumnType(columnType);
        }
    }

    private static IConventionPropertyBuilder Column(IConventionEntityTypeBuilder entity, Type type, string name, string column)
    {
        var property = entity.IndexerProperty(type, name)
            ?? throw new InvalidOperationException($"The full-text column '{name}' could not be added to '{entity.Metadata.Name}'.");

        property.HasColumnName(column);
        return property;
    }
}

/// <summary>
/// The SQL functions the query rewrite leaves behind, translated to FTS5's auxiliary functions. Never executed.
/// </summary>
internal static class FullTextFunctions
{
    public static readonly MethodInfo HighlightMethod = typeof(FullTextFunctions).GetMethod(nameof(Highlight))!;

    public static readonly MethodInfo SnippetMethod = typeof(FullTextFunctions).GetMethod(nameof(Snippet))!;

    public static string? Highlight(string? index, int column, string open, string close) =>
        throw FullText.OutsideQuery(nameof(FullText.Highlight));

    public static string? Snippet(string? index, int column, string open, string close, string ellipsis, int tokens) =>
        throw FullText.OutsideQuery(nameof(FullText.Snippet));
}

/// <summary>Translates <see cref="FullTextFunctions"/> to <c>highlight(...)</c> and <c>snippet(...)</c>.</summary>
internal sealed class FullTextFunctionTranslatorPlugin(ISqlExpressionFactory sql) : IMethodCallTranslatorPlugin
{
    public IEnumerable<IMethodCallTranslator> Translators { get; } = [new Translator(sql)];

    private sealed class Translator(ISqlExpressionFactory sql) : IMethodCallTranslator
    {
        public SqlExpression? Translate(
            SqlExpression? instance,
            MethodInfo method,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            var name = method == FullTextFunctions.HighlightMethod ? "highlight"
                : method == FullTextFunctions.SnippetMethod ? "snippet"
                : null;

            // The first argument is FTS5's hidden table column, which the auxiliary function reads as a handle to the
            // current match rather than as a value — so it is passed as the column, untouched.
            return name is null
                ? null
                : sql.Function(
                    name,
                    arguments,
                    nullable: true,
                    argumentsPropagateNullability: arguments.Select(static _ => false),
                    typeof(string));
        }
    }
}
