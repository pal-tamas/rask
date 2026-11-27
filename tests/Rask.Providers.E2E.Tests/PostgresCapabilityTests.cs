using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Rask.Providers.E2E.Tests;

public sealed class Tariff
{
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";
}

public sealed class Booking
{
    public int Id { get; set; }

    public string TariffCode { get; set; } = "";

    public string Room { get; set; } = "";

    public DateOnly From { get; set; }

    public DateOnly? To { get; set; }
}

/// <summary>A model whose text compares case-insensitively, with a text key and a foreign key to it.</summary>
public sealed class CapabilityDbContext(DbContextOptions<CapabilityDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_capabilities";

    public DbSet<Tariff> Tariffs => Set<Tariff>();

    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.OrderTextBy("Hungarian_100_CI_AS");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Tariff>().HasKey(t => t.Code);
        modelBuilder.Entity<Booking>().HasOne<Tariff>().WithMany().HasForeignKey(b => b.TariffCode);
    }
}

/// <summary>
///     What PostgreSQL itself does with the text Rask gives it: the findings the overlap rule, the key collation and the
///     trailing-space CHECK rest on, held to a real server.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresCapabilityTests() : ParityTests<CapabilityDbContext>(ProviderLeg.Postgres(CapabilityDbContext.Schema))
{
    private const string ExclusionViolation = "23P01";

    [Fact]
    public async Task A_gist_exclusion_compares_a_rask_text_key_as_the_column_does()
    {
        RequireServer();
        await using var db = Open();
        await Sql(db, """
            CREATE EXTENSION IF NOT EXISTS btree_gist;
            ALTER TABLE rask_capabilities."Bookings" ADD CONSTRAINT "EX_Bookings_Room"
                EXCLUDE USING gist ("Room" WITH =, daterange("From", "To", '[)') WITH &&);
            """);
        await Book(db, "Kék szoba", 1, 3);

        var sameRoomOtherCase = await Assert.ThrowsAsync<PostgresException>(() => Book(db, "KÉK SZOBA", 2, 4));
        await Book(db, "Kek szoba", 2, 4);
        await Book(db, "KÉK SZOBA", 3, 5);
        var openEnded = await Assert.ThrowsAsync<PostgresException>(() => Book(db, "kék szoba", 4, null));

        Assert.Equal(ExclusionViolation, sameRoomOtherCase.SqlState);
        Assert.Equal("EX_Bookings_Room", sameRoomOtherCase.ConstraintName);
        Assert.Equal(ExclusionViolation, openEnded.SqlState);
    }

    [Fact]
    public async Task A_text_key_and_the_foreign_key_to_it_are_binary_while_other_text_is_rask_text()
    {
        RequireServer();
        await using var db = Open();

        var collations = await Rows(db, """
            SELECT table_name || '.' || column_name || '=' || coalesce(collation_name, '') AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'rask_capabilities' AND data_type = 'text'
            ORDER BY 1;
            """);

        Assert.Equal(
            ["Bookings.Room=rask_text", "Bookings.TariffCode=C", "Tariffs.Code=C", "Tariffs.Name=rask_text"],
            collations);
    }

    [Fact]
    public async Task A_trailing_space_tells_two_rask_text_values_apart()
    {
        RequireServer();
        await using var db = Open();

        var equal = await Rows(db, "SELECT ('ABC' COLLATE rask_text = 'abc ' COLLATE rask_text)::text AS \"Value\";");

        Assert.Equal(["false"], equal);
    }

    private protected override CapabilityDbContext Open() => Leg.Open<CapabilityDbContext>(o => new CapabilityDbContext(o));

    private static async Task Book(CapabilityDbContext db, string room, int fromDay, int? toDay)
    {
        await Sql(db, """INSERT INTO rask_capabilities."Tariffs" ("Code", "Name") VALUES ('T', 'T') ON CONFLICT DO NOTHING;""");
        var day = new DateOnly(2026, 1, 1);
        db.Bookings.Add(new Booking { TariffCode = "T", Room = room, From = day.AddDays(fromDay), To = toDay is { } to ? day.AddDays(to) : null });
        try
        {
            await db.SaveChangesAsync(Ct);
        }
        catch (DbUpdateException failed) when (failed.InnerException is PostgresException postgres)
        {
            db.ChangeTracker.Clear();
            throw postgres;
        }
    }

    private static async Task Sql(DbContext db, string sql)
    {
        // Constant DDL written by the fact itself.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(sql, Ct);
#pragma warning restore EF1002
    }

    private static async Task<List<string>> Rows(DbContext db, string sql) =>
        await db.Database.SqlQueryRaw<string>(sql).ToListAsync(Ct);
}
