using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     The few words of SQL a provider-neutral model declaration needs, in the dialect of the provider the model is
///     built for: a column's quoted name, a value's literal, text joined and a number written as text, and the
///     database's clock.
/// </summary>
/// <remarks>
///     Quoting and literals are the provider's own (<see cref="ISqlGenerationHelper" />, a type mapping's
///     <c>GenerateSqlLiteral</c>), so a strongly-typed id or an enum is written as the column stores it. What is left
///     differs between SQL Server, PostgreSQL and SQLite, and is written here once.
/// </remarks>
internal sealed class ModelSql
{
    private readonly ISqlGenerationHelper _helper;
    private readonly IRelationalTypeMappingSource _mappings;

    private ModelSql(string provider, ISqlGenerationHelper helper, IRelationalTypeMappingSource mappings)
    {
        Provider = provider;
        _helper = helper;
        _mappings = mappings;
    }

    /// <summary>The EF Core provider's name, as <c>Database.ProviderName</c> reports it.</summary>
    internal string Provider { get; }

    /// <summary>Whether the model is built for SQL Server, whose filtered index takes only the plainest predicate.</summary>
    internal bool IsSqlServer => string.Equals(Provider, ProviderNames.SqlServer, StringComparison.Ordinal);

    /// <summary>The dialect of the provider <paramref name="services" /> belong to, or null for one that writes no SQL.</summary>
    /// <param name="services">The context's internal services, as a convention factory receives them.</param>
    internal static ModelSql? For(IServiceProvider services) =>
        services.GetService<IDatabaseProvider>()?.Name is { } provider
        && services.GetService<ISqlGenerationHelper>() is { } helper
        && services.GetService<IRelationalTypeMappingSource>() is { } mappings
            ? new ModelSql(provider, helper, mappings)
            : null;

    /// <summary>
    ///     Refuses a provider Rask has no dialect for: a declaration that would be written in the wrong SQL is better
    ///     refused where the model is built.
    /// </summary>
    /// <param name="declaration">The declaration, as the app wrote it.</param>
    internal void Supported(string declaration)
    {
        if (Provider is not (ProviderNames.SqlServer or ProviderNames.Postgres or ProviderNames.Sqlite))
        {
            throw new InvalidOperationException(
                $"{declaration} is written for SQL Server, PostgreSQL and SQLite, and the model is built for {Provider}. "
                + "Say it in that provider's own SQL instead — HasComputedColumnSql, HasDefaultValueSql or HasFilter.");
        }
    }

    /// <summary>A column's name, quoted.</summary>
    internal string Column(string name) => _helper.DelimitIdentifier(name);

    /// <summary>
    ///     <paramref name="value" /> as a literal: through <paramref name="mapping" /> — a column's, so its converter
    ///     applies — or the default mapping of the value's type.
    /// </summary>
    internal string Literal(object? value, RelationalTypeMapping? mapping = null)
    {
        if (value is null)
        {
            return "NULL";
        }

        mapping ??= _mappings.FindMapping(value.GetType())
                    ?? throw new InvalidOperationException($"{Provider} has no column type for {value.GetType().Name}.");
        return mapping.GenerateSqlLiteral(value);
    }

    /// <summary>
    ///     A column's default constant as its SQL: <paramref name="value" /> through the column's converter, or — when
    ///     <paramref name="stored" /> — already as the column stores it.
    /// </summary>
    /// <remarks>
    ///     A SQL Server bit is written bare (<c>0</c>), as a hand-written default and the one a legacy schema carries say
    ///     it, so a model moving from <c>HasDefaultValueSql("0")</c> to <c>Default(x =&gt; x.Flag, false)</c> changes no
    ///     column.
    /// </remarks>
    internal string Default(object? value, RelationalTypeMapping? mapping, bool stored)
    {
        if (value is null)
        {
            return "NULL";
        }

        var provider = stored || mapping?.Converter is null ? value : mapping.Converter.ConvertToProvider(value);
        return provider switch
        {
            null => "NULL",
            bool flag when IsSqlServer => flag ? "1" : "0",
            _ when mapping is not null => mapping.GenerateProviderValueSqlLiteral(provider),
            _ => Literal(provider),
        };
    }

    /// <summary>
    ///     The database's wall clock — its current local time, in the zone the database server runs in — as a
    ///     <see cref="DateTime" /> column holds it.
    /// </summary>
    /// <remarks>
    ///     <c>CURRENT_TIMESTAMP</c> on SQL Server (the server's local time, as a legacy schema's default writes it),
    ///     <c>LOCALTIMESTAMP</c> on PostgreSQL (the session's zone), and SQLite's local time in the text a
    ///     <see cref="DateTime" /> is stored as there.
    /// </remarks>
    internal string Wall() =>
        Provider switch
        {
            ProviderNames.SqlServer => "CURRENT_TIMESTAMP",
            ProviderNames.Sqlite => "strftime('%Y-%m-%d %H:%M:%f', 'now', 'localtime')",
            _ => "LOCALTIMESTAMP",
        };

    /// <summary>
    ///     How <paramref name="property" />'s column stores a value — its converter included, so a value object or a
    ///     strongly-typed id beside it is written as the column holds it — or null where the provider has no mapping.
    /// </summary>
    /// <remarks>
    ///     As the model is finalized a column's mapping may not be settled yet: then it is the provider's mapping of
    ///     what the converter stores, the converter composed before it.
    /// </remarks>
    internal RelationalTypeMapping? Mapping(IReadOnlyProperty property)
    {
        if (property.FindTypeMapping() is RelationalTypeMapping mapping)
        {
            return mapping;
        }

        if (property.GetValueConverter() is not { } converter)
        {
            return null;
        }

        var stored = _mappings.FindMapping(converter.ProviderClrType, property.GetColumnType())
                     ?? _mappings.FindMapping(converter.ProviderClrType);
        return (RelationalTypeMapping?)stored?.WithComposedConverter(converter, null, null, null, null);
    }

    /// <summary>
    ///     How a column holding the one value of a value object stores that value: the mapping of
    ///     <paramref name="held" />, then <paramref name="through" /> — the provider's own conversion of it — when there
    ///     is one.
    /// </summary>
    internal RelationalTypeMapping? Held(Type held, string? storeType, ValueConverter? through)
    {
        var mapping = (storeType is null ? null : _mappings.FindMapping(held, storeType)) ?? _mappings.FindMapping(held);
        return through is null || mapping is null
            ? mapping
            : (RelationalTypeMapping)mapping.WithComposedConverter(through, null, null, null, null);
    }

    /// <summary>The literal a boolean column is compared with in a filtered index.</summary>
    /// <remarks>SQL Server takes no <c>CAST</c> in an index filter, so its bit is written bare there.</remarks>
    internal string FilterBoolean(bool value)
    {
        if (!IsSqlServer)
        {
            return Literal(value);
        }

        return value ? "1" : "0";
    }

    /// <summary>Text parts joined into one.</summary>
    internal string Concat(IEnumerable<string> parts) =>
        "(" + string.Join(IsSqlServer ? " + " : " || ", parts) + ")";

    /// <summary>A whole number written as text, as C#'s <c>ToString()</c> writes it.</summary>
    internal string Text(string number) =>
        IsSqlServer ? $"CAST({number} AS nvarchar(20))" : $"CAST({number} AS TEXT)";

    /// <summary>
    ///     The database's clock, in UTC, as a column of <paramref name="clrType" /> stored as
    ///     <paramref name="storeType" /> holds it.
    /// </summary>
    /// <param name="clrType">The property's type: a <see cref="DateTimeOffset" /> or a <see cref="DateTime" />.</param>
    /// <param name="storeType">The column's type in the database.</param>
    internal string Now(Type clrType, string? storeType)
    {
        var offset = clrType == typeof(DateTimeOffset);
        return Provider switch
        {
            ProviderNames.SqlServer => offset ? "TODATETIMEOFFSET(SYSUTCDATETIME(), 0)" : "SYSUTCDATETIME()",

            // The text EF Core's SQLite provider writes a DateTime (and DateTimeOffset) as, so a row the database
            // stamped reads back like one Rask wrote.
            ProviderNames.Sqlite => offset
                ? "strftime('%Y-%m-%d %H:%M:%f', 'now') || '+00:00'"
                : "strftime('%Y-%m-%d %H:%M:%f', 'now')",

            _ => storeType?.StartsWith("timestamp without", StringComparison.OrdinalIgnoreCase) == true
                 || string.Equals(storeType, "timestamp", StringComparison.OrdinalIgnoreCase)
                ? "(now() AT TIME ZONE 'UTC')"
                : "now()",
        };
    }
}
