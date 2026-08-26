using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.SQLite.Tests;

// The decimal collating sequence, exercised through the raw-ADO factory and directly against a live
// connection. (The EF Core side — that UseRaskSqlite makes OrderBy correct on every locale — is covered
// in Rask.SQLite.EntityFrameworkCore.Tests.RaskSqliteDecimalOrderingTests.)
//
// The assembly disables parallelisation (see AssemblyInfo.cs), so swapping CurrentCulture is safe here.
public sealed class SqliteCollationsTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"rask-sqlite-collation-{Guid.NewGuid():N}.db");

    [Theory]
    [InlineData("")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("en-HU")]
    [InlineData("fr-FR")]
    public async Task The_factory_registers_a_decimal_collation_that_sorts_numerically_on_every_locale(string culture)
    {
        await SeedAsync("2.00", "19.95", "9.50", "100.50", "10.00");

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(culture);
        try
        {
            var services = new ServiceCollection();
            services.AddRaskSqlite($"Data Source={_dbPath}");
            await using var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IRaskSqliteConnectionFactory>();

            await using var connection = await factory.CreateOpenAsync();

            Assert.Equal(
                ["2.00", "9.50", "10.00", "19.95", "100.50"],
                Query(connection, $"SELECT v FROM amounts ORDER BY v COLLATE {SqliteCollations.Decimal}"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // Without a collation SQLite compares the stored text byte by byte, which is the behaviour the
    // collation exists to replace. Pinned so the test above cannot pass vacuously.
    [Fact]
    public async Task Without_the_collation_the_same_column_sorts_lexicographically()
    {
        await SeedAsync("2.00", "19.95", "9.50", "100.50", "10.00");

        await using var connection = await OpenPlainAsync();

        Assert.Equal(
            ["10.00", "100.50", "19.95", "2.00", "9.50"],
            Query(connection, "SELECT v FROM amounts ORDER BY v"));
    }

    // A collation runs inside SQLite's native comparison loop, where a managed exception cannot be
    // unwound and terminates the process. Everything that does not parse must therefore be ordered,
    // never rejected.
    [Fact]
    public async Task Unparseable_text_is_ordered_after_every_number_instead_of_throwing()
    {
        await SeedAsync("9.50", "lots", "2.00", "", "1e40", "99999999999999999999999999999999999999999999");

        await using var connection = await OpenPlainAsync();
        SqliteCollations.Apply(connection);

        var ordered = Query(connection, $"SELECT v FROM amounts ORDER BY v COLLATE {SqliteCollations.Decimal}");

        // The two parseable values lead, in numeric order. The rest — junk, an exponent form the
        // invariant fixed-point parse rejects, and a value past decimal's range — follow ordinally.
        Assert.Equal(["2.00", "9.50"], ordered[..2]);
        Assert.Equal(["", "1e40", "99999999999999999999999999999999999999999999", "lots"], ordered[2..]);
    }

    // Values differing only in trailing zeros are numerically equal, so the collation reports them equal —
    // which is what makes GROUP BY and DISTINCT on a decimal behave like decimal rather than like text.
    [Fact]
    public async Task Trailing_zeros_compare_equal()
    {
        await SeedAsync("19.95", "19.950", "19.9500");

        await using var connection = await OpenPlainAsync();
        SqliteCollations.Apply(connection);

        Assert.Equal(
            ["1"],
            Query(connection, $"SELECT COUNT(DISTINCT v COLLATE {SqliteCollations.Decimal}) FROM amounts"));
    }

    // Applying it twice on the same connection must be idempotent — the interceptor and the factory
    // both call it, and it runs again on every pooled re-open.
    [Fact]
    public async Task Applying_the_collation_twice_is_idempotent()
    {
        await SeedAsync("2.00", "10.00", "9.50");

        await using var connection = await OpenPlainAsync();
        SqliteCollations.Apply(connection);
        SqliteCollations.Apply(connection);

        Assert.Equal(
            ["2.00", "9.50", "10.00"],
            Query(connection, $"SELECT v FROM amounts ORDER BY v COLLATE {SqliteCollations.Decimal}"));
    }

    private async Task<SqliteConnection> OpenPlainAsync()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        return connection;
    }

    private async Task SeedAsync(params string[] values)
    {
        await using var connection = await OpenPlainAsync();

        await using (var ddl = connection.CreateCommand())
        {
            ddl.CommandText = "CREATE TABLE IF NOT EXISTS amounts (v TEXT NOT NULL)";
            await ddl.ExecuteNonQueryAsync();
        }

        foreach (var value in values)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO amounts (v) VALUES ($v)";
            insert.Parameters.AddWithValue("$v", value);
            await insert.ExecuteNonQueryAsync();
        }
    }

    private static List<string> Query(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }
}
