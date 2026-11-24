using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Rask.Data;

/// <summary>
///     The next value of a key's sequence, taken from the database as the row is added: <c>NEXT VALUE FOR</c> on SQL
///     Server, <c>nextval</c> on PostgreSQL, and a row of <c>__RaskSequences</c> advanced in one statement on SQLite.
/// </summary>
/// <param name="name">The sequence.</param>
/// <param name="start">Its first value.</param>
/// <param name="property">The key it fills, for the type it is held in and, on SQLite, the table it starts above.</param>
internal sealed class SequenceValueGenerator(string name, long start, IProperty property) : ValueGenerator
{
    /// <summary>SQLite's stand-in for a sequence: one row per sequence, holding the value it last handed out.</summary>
    internal const string SqliteTable = "__RaskSequences";

    /// <inheritdoc />
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc />
    protected override object? NextValue(EntityEntry entry)
    {
        var (setup, next, parameters) = Statements(entry.Context);
        if (setup is not null)
        {
            entry.Context.Database.ExecuteSqlRaw(setup);
        }

        return InKeyType(entry.Context.Database.SqlQueryRaw<long>(next, parameters).AsEnumerable().First());
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> NextValueAsync(EntityEntry entry, CancellationToken cancellationToken = default)
    {
        var (setup, next, parameters) = Statements(entry.Context);
        if (setup is not null)
        {
            await entry.Context.Database.ExecuteSqlRawAsync(setup, cancellationToken).ConfigureAwait(false);
        }

        var value = await entry.Context.Database.SqlQueryRaw<long>(next, parameters)
            .AsAsyncEnumerable().FirstAsync(cancellationToken).ConfigureAwait(false);
        return InKeyType(value);
    }

    // In the provider's own words; the names are the model's, quoted by the provider, and every value a parameter.
    private (string? Setup, string Next, object[] Parameters) Statements(DbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var schema = context.Model.GetDefaultSchema();

        switch (context.Database.ProviderName)
        {
            case ProviderNames.SqlServer:
                return (null, $"SELECT NEXT VALUE FOR {sql.DelimitIdentifier(name, schema)} AS {sql.DelimitIdentifier("Value")}", []);

            case ProviderNames.Postgres:
                return (null, $"SELECT nextval({{0}}::regclass) AS {sql.DelimitIdentifier("Value")}", [sql.DelimitIdentifier(name, schema)]);

            case ProviderNames.Sqlite:
                var table = sql.DelimitIdentifier(SqliteTable);
                var keyTable = sql.DelimitIdentifier(property.DeclaringType.ContainingEntityType.GetTableName()!);
                var keyColumn = sql.DelimitIdentifier(property.GetColumnName());
                return (
                    $"CREATE TABLE IF NOT EXISTS {table} (\"Name\" TEXT NOT NULL PRIMARY KEY, \"Next\" INTEGER NOT NULL)",
                    $"INSERT INTO {table} (\"Name\", \"Next\") " +
                    $"VALUES ({{0}}, MAX({{1}}, COALESCE((SELECT MAX({keyColumn}) FROM {keyTable}), 0) + 1)) " +
                    $"ON CONFLICT (\"Name\") DO UPDATE SET \"Next\" = \"Next\" + 1 RETURNING \"Next\" AS \"Value\"",
                    [name, start]);

            default:
                throw new InvalidOperationException(
                    $"FromSequence(\"{name}\") is written for SQL Server, PostgreSQL and SQLite, and the context runs on " +
                    $"{context.Database.ProviderName}. Generate the key that provider's own way.");
        }
    }

    // The value in the key's own type: an int, a long, or the strongly-typed id its converter reads one into.
    private object InKeyType(long value)
    {
        if (property.GetValueConverter() is { } converter)
        {
            return converter.ConvertFromProvider(converter.ProviderClrType == typeof(int) ? (object)checked((int)value) : value)!;
        }

        return property.ClrType == typeof(int) ? (object)checked((int)value) : value;
    }
}
