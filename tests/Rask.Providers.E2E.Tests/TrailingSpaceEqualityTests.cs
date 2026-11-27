using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>A model with nothing in it but the app's text order, for a question asked of the database itself.</summary>
public sealed class TrailingSpaceDbContext(DbContextOptions<TrailingSpaceDbContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.OrderTextBy("Hungarian_100_CI_AS");
}

/// <summary>
///     Whether <c>'ABC' = 'abc '</c> under the app's text order: SQL Server pads the shorter text with spaces before it
///     compares, SQLite's registered collation and PostgreSQL's <c>rask_text</c> do not (see
///     <see cref="PostgresCapabilityTests" />). That difference is why a stored text never ends with a space.
/// </summary>
public abstract class TrailingSpaceEqualityTests(ProviderLeg leg, string sql, string expected) : ParityTests<TrailingSpaceDbContext>(leg)
{
    [Fact]
    public async Task A_trailing_space_is_or_is_not_padded_away_as_the_provider_compares()
    {
        RequireServer();
        await using var db = Open();
        await db.Database.OpenConnectionAsync(Ct);

        var equal = await db.Database.SqlQueryRaw<string>(sql).ToListAsync(Ct);

        Assert.Equal([expected], equal);
    }

    private protected override TrailingSpaceDbContext Open() => Leg.Open<TrailingSpaceDbContext>(o => new TrailingSpaceDbContext(o));
}

public sealed class SqliteTrailingSpaceEqualityTests() : TrailingSpaceEqualityTests(
    ProviderLeg.Sqlite("trailing-space"),
    "SELECT CASE WHEN 'ABC' = 'abc ' COLLATE RASK_TEXT THEN 'true' ELSE 'false' END AS Value",
    "false");

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerTrailingSpaceEqualityTests() : TrailingSpaceEqualityTests(
    ProviderLeg.SqlServer("rask_e2e_trailing_space"),
    "SELECT CASE WHEN N'ABC' = N'abc ' THEN 'true' ELSE 'false' END AS Value",
    "true");
