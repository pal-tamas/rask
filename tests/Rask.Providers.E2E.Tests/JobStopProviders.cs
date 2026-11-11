using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

public sealed class PgStopDbContext(DbContextOptions<PgStopDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_e2e_jobstop";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddRaskJobs();
    }
}

public sealed class PgStopBeforeDbContext(DbContextOptions<PgStopBeforeDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(PgStopDbContext.Schema);
        modelBuilder.AddRaskJobs();
        BeforeJobStop.Shape(modelBuilder);
    }
}

public sealed class MsStopDbContext(DbContextOptions<MsStopDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskJobs();
}

public sealed class MsStopBeforeDbContext(DbContextOptions<MsStopBeforeDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddRaskJobs();
        BeforeJobStop.Shape(modelBuilder);
    }
}

/// <summary>Stop, limits, the watch and the lane view on PostgreSQL: an integer and a timestamptz, kept to the microsecond.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresJobStopTests : JobStopProviderTests<PgStopDbContext, PgStopBeforeDbContext>
{
    private protected override bool Available => Postgres.Available;

    private protected override string SkipReason => Postgres.SkipReason;

    private protected override string TableSchema => PgStopDbContext.Schema;

    private protected override (string Stopped, string StartedAt) ColumnTypes => ("integer", "timestamp with time zone");

    private protected override TimeSpan Precision => TimeSpan.FromMicroseconds(1);

    private protected override void Use(DbContextOptionsBuilder builder) => builder.UseRaskPostgresAt(Postgres.Required);

    private protected override PgStopDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PgStopDbContext>().UseRaskPostgresAt(Postgres.Required).Options);

    private protected override PgStopBeforeDbContext NewContextBeforeUpgrade() =>
        new(new DbContextOptionsBuilder<PgStopBeforeDbContext>().UseRaskPostgresAt(Postgres.Required).Options);

    private protected override Task Recreate(DbContext db) => Postgres.ResetSchemaAsync(db, PgStopDbContext.Schema);

    private protected override Task Drop(DbContext db) => Postgres.DropSchemaAsync(db, PgStopDbContext.Schema);
}

/// <summary>Stop, limits, the watch and the lane view on SQL Server: an int and a datetime2, kept to the tick.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerJobStopTests : JobStopProviderTests<MsStopDbContext, MsStopBeforeDbContext>
{
    private const string DatabaseName = "rask_e2e_jobstop";

    private protected override bool Available => SqlServer.Available;

    private protected override string SkipReason => SqlServer.SkipReason;

    private protected override string TableSchema => "dbo";

    private protected override (string Stopped, string StartedAt) ColumnTypes => ("int", "datetime2");

    private protected override TimeSpan Precision => TimeSpan.FromTicks(1);

    private protected override void Use(DbContextOptionsBuilder builder) => builder.UseRaskSqlServerAt(SqlServer.Database(DatabaseName));

    private protected override MsStopDbContext NewContext() =>
        new(new DbContextOptionsBuilder<MsStopDbContext>().UseRaskSqlServerAt(SqlServer.Database(DatabaseName)).Options);

    private protected override MsStopBeforeDbContext NewContextBeforeUpgrade() =>
        new(new DbContextOptionsBuilder<MsStopBeforeDbContext>().UseRaskSqlServerAt(SqlServer.Database(DatabaseName)).Options);

    private protected override async Task Recreate(DbContext db)
    {
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    private protected override Task Drop(DbContext db) => db.Database.EnsureDeletedAsync();
}
