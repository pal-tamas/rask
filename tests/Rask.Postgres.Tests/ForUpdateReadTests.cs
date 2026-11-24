using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Postgres.Tests;

/// <summary>
/// The read <c>LockedBy(…, forUpdate: true)</c> makes of a save's parents, as PostgreSQL is sent it — checked as SQL,
/// offline: the query ends <c>FOR UPDATE OF</c> the parents' alias, so only their rows are locked.
/// </summary>
public sealed class ForUpdateReadTests
{
    [Fact]
    public void The_parents_rows_are_read_for_update()
    {
        using var db = Open();

        var sql = ForUpdateSql.Rewrite(db.Database.ProviderName, ParentLock<Period>.ForUpdate(db, Key(db), [3, 4]).ToQueryString());

        Assert.Contains("FROM \"Periods\" AS p", sql, StringComparison.Ordinal);
        Assert.EndsWith("\nFOR UPDATE OF p", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_query_not_tagged_for_update_is_sent_as_written()
    {
        using var db = Open();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = db.Set<Period>().Where(p => p.Closed).ToQueryString();
        var written = command.CommandText;

        ForUpdateSql.Apply(command, db);

        Assert.Equal(written, command.CommandText);
    }

    private static Microsoft.EntityFrameworkCore.Metadata.IProperty Key(DbContext db) =>
        db.Model.FindEntityType(typeof(Period))!.FindPrimaryKey()!.Properties[0];

    private static PeriodContext Open() =>
        new(new DbContextOptionsBuilder<PeriodContext>().UseNpgsql("Host=localhost;Database=rask").Options);

    public sealed class Period
    {
        public int Id { get; set; }

        public bool Closed { get; set; }
    }

    private sealed class PeriodContext(DbContextOptions<PeriodContext> options) : DbContext(options)
    {
        public DbSet<Period> Periods => Set<Period>();
    }
}
