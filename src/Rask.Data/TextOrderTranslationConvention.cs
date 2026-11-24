using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     Teaches the model what <see cref="TextOrderExtensions.InTextOrder" /> is written as in SQL — and, on a database
///     that compares text by a collation each connection registers, gives every text column that collation.
/// </summary>
/// <remarks>
///     <para>
///         On SQL Server the collation reaches the <c>ORDER BY</c> that asks for it and nothing else: the database's own
///         default collation already compares every column the way the name says.
///     </para>
///     <para>
///         SQLite has no such default, so where the provider registers the collation on each connection
///         (<see cref="ITextCollationEnforcer" />, which <c>UseRaskSqlite</c> adds) every text column is created with it:
///         equality, a unique index, <c>GROUP BY</c> and <c>ORDER BY</c> then compare as SQL Server does. PostgreSQL through
///         <c>UseRaskPostgres</c> does the same with a nondeterministic ICU collation its migrations create; a plain
///         <c>UseNpgsql</c> orders by the language's ICU collation and leaves the columns alone. Read once, as the model is
///         built.
///     </para>
/// </remarks>
internal sealed class TextOrderTranslationConvention : IModelInitializedConvention, IModelFinalizingConvention
{
    private readonly string? _orderedBy;
    private readonly string? _columns;
    private readonly string? _annotated;

    /// <summary>The convention, as <see cref="For" /> reads it off the provider.</summary>
    /// <param name="orderedBy">The collation <c>InTextOrder()</c> writes, or null to order by the column as it is.</param>
    /// <param name="columns">The collation every text column gets, or null to leave the columns as they are.</param>
    /// <param name="annotated">The SQL Server name the model carries for the connections that register it, or null.</param>
    private TextOrderTranslationConvention(string? orderedBy, string? columns, string? annotated)
    {
        _orderedBy = orderedBy;
        _columns = columns;
        _annotated = annotated;
    }

    /// <summary>The convention for <paramref name="collation" /> on the provider <paramref name="services" /> belong to.</summary>
    /// <exception cref="InvalidOperationException">Off SQL Server, the name is not one Rask can read.</exception>
    internal static TextOrderTranslationConvention For(string collation, IServiceProvider services)
    {
        var provider = services.GetService<IDatabaseProvider>()?.Name;
        if (string.Equals(provider, ProviderNames.SqlServer, StringComparison.Ordinal))
        {
            return new TextOrderTranslationConvention(collation, columns: null, annotated: null);
        }

        if (string.Equals(provider, ProviderNames.Postgres, StringComparison.Ordinal))
        {
            // Through UseRaskPostgres, a comparison that ignores case or accents is a nondeterministic collation its
            // migrations create; anywhere else — or for one that tells them apart — the language's ICU order is enough.
            var parsed = TextCollation.Parse(collation);
            return Enforcer(services) is { } postgres && parsed.NondeterministicIcuLocale is not null
                ? new TextOrderTranslationConvention(postgres.Collation, postgres.Collation, collation)
                : new TextOrderTranslationConvention(parsed.IcuCollation, columns: null, annotated: null);
        }

        // A plain UseSqlite — a browser app's — registers nothing on its connections, so there a column named with the
        // collation could not be read back: it keeps ordering by the column as it is.
        if (string.Equals(provider, ProviderNames.Sqlite, StringComparison.Ordinal) && Enforcer(services) is { } enforcer)
        {
            _ = TextCollation.Parse(collation);
            return new TextOrderTranslationConvention(enforcer.Collation, enforcer.Collation, collation);
        }

        return new TextOrderTranslationConvention(orderedBy: null, columns: null, annotated: null);
    }

    private static ITextCollationEnforcer? Enforcer(IServiceProvider services) =>
        services.GetService<IDbContextOptions>()?.Extensions.OfType<ITextCollationEnforcer>().FirstOrDefault();

    public void ProcessModelInitialized(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var function = modelBuilder.HasDbFunction(new Func<string, string>(TextOrderExtensions.InTextOrder).Method);

        function?.HasTranslation(_orderedBy is { } name
            ? arguments => new CollateExpression(arguments[0], name)
            : static arguments => arguments[0]);

        if (_annotated is not null)
        {
            modelBuilder.HasAnnotation(TextCollation.Annotation, _annotated);
        }
    }

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        if (_columns is null)
        {
            return;
        }

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().Where(static e => !e.IsMappedToJson()))
        {
            Collate(entityType.GetDeclaredProperties(), entityType.GetDeclaredComplexProperties());
        }
    }

    // A column the model gave a collation of its own keeps it; a JSON document's members are no columns at all.
    private void Collate(IEnumerable<IConventionProperty> properties, IEnumerable<IConventionComplexProperty> complexProperties)
    {
        foreach (var property in properties.Where(static p => IsText(p) && p.GetCollation() is null))
        {
            property.Builder.UseCollation(_columns);
        }

        foreach (var type in complexProperties.Select(static c => c.ComplexType).Where(static t => !t.IsMappedToJson()))
        {
            Collate(type.GetDeclaredProperties(), type.GetDeclaredComplexProperties());
        }
    }

    private static bool IsText(IReadOnlyProperty property) =>
        (property.GetValueConverter()?.ProviderClrType ?? property.GetProviderClrType() ?? property.ClrType) == typeof(string);
}
