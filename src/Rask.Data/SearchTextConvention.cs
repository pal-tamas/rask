using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     Teaches the model what <see cref="SearchTextExtensions.Loosely" /> is written as in SQL: the provider's
///     case- and accent-insensitive collation, in the query alone.
/// </summary>
/// <remarks>
///     SQL Server has one built in (<see cref="SqlServerCollation" />); SQLite and PostgreSQL through Rask's own options
///     have the one their <see cref="ITextCollationEnforcer" /> names. Anywhere else the query is refused with the reason,
///     rather than compared as the column is and finding less than it should.
/// </remarks>
internal sealed class SearchTextConvention : IModelInitializedConvention
{
    /// <summary>SQL Server's collation: no one language's order, case, accents, kana and width aside.</summary>
    internal const string SqlServerCollation = "Latin1_General_100_CI_AI";

    private readonly string? _collation;
    private readonly string? _provider;

    private SearchTextConvention(string? collation, string? provider)
    {
        _collation = collation;
        _provider = provider;
    }

    /// <summary>The convention for the provider <paramref name="services" /> belong to.</summary>
    internal static SearchTextConvention For(IServiceProvider services)
    {
        var provider = services.GetService<IDatabaseProvider>()?.Name;
        if (string.Equals(provider, ProviderNames.SqlServer, StringComparison.Ordinal))
        {
            return new SearchTextConvention(SqlServerCollation, provider);
        }

        var enforcer = services.GetService<IDbContextOptions>()?.Extensions.OfType<ITextCollationEnforcer>().FirstOrDefault();
        return new SearchTextConvention(enforcer?.SearchCollation, provider);
    }

    public void ProcessModelInitialized(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var function = modelBuilder.HasDbFunction(new Func<string, string>(SearchTextExtensions.Loosely).Method);
        if (_collation is { } collation)
        {
            function?.HasTranslation(arguments => new CollateExpression(arguments[0], collation));
            return;
        }

        var provider = _provider ?? "this provider";
        function?.HasTranslation(_ => throw new InvalidOperationException(
            $"x.Loosely() compares text case and accents aside, and {provider} has no collation Rask knows to do it with. "
            + "Open the context with UseRaskSqlServer, UseRaskPostgres or UseRaskSqlite — or, in a browser, "
            + "UseSqlite(…).UseRaskFullTextSearch()."));
    }
}
