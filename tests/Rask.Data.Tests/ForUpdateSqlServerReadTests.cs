using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
/// The read <c>LockedBy(…, forUpdate: true)</c> makes of a save's parents, as SQL Server is sent it — checked as SQL,
/// offline: the parents' table takes <c>UPDLOCK, HOLDLOCK</c>, held to the end of the save's transaction.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ForUpdateSqlServerReadTests
{
    [Fact]
    public void The_parents_table_is_read_with_an_update_lock_held_to_the_end_of_the_transaction()
    {
        using var db = Open();

        var sql = ForUpdateSql.Rewrite(db.Database.ProviderName, ParentRead<Period>.ForUpdate(db, Key(db), [3, 4]).ToQueryString());

        Assert.Contains("FROM [Periods] AS [p] WITH (UPDLOCK, HOLDLOCK)", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [p].[Id] = ", sql, StringComparison.Ordinal);
        Assert.Equal(2, sql.Split("WITH (UPDLOCK").Length);
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
        new(OptionsLayout.LikeAddDbContext<PeriodContext>()
            .UseSqlServer("Server=localhost;Database=rask;User Id=sa;Password=x;TrustServerCertificate=true")
            .Options);

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
