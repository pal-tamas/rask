using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Providers.E2E.Tests.Fuel;

/// <summary>A delivery note of an existing app: numbered tenants, read only, no framework columns.</summary>
public sealed class DeliveryNote : Aggregate<int>
{
    public const Tenancy Scope = Tenancy.PerTenant;
    public const ModelWrites Writes = ModelWrites.None;
    public const Timestamps Stamps = Timestamps.None;
    public const Concurrency Checks = Concurrency.None;

    public int? TenantId { get; private set; }

    public string Number { get; private set; } = "";

    public IReadOnlyList<DeliveryNoteTransaction> Fuellings { get; private set; } = [];
}

/// <summary>A fuel transaction of the same app.</summary>
public sealed class Transaction : Aggregate<int>
{
    public const Tenancy Scope = Tenancy.PerTenant;
    public const ModelWrites Writes = ModelWrites.None;
    public const Timestamps Stamps = Timestamps.None;
    public const Concurrency Checks = Concurrency.None;

    public int? TenantId { get; private set; }

    public int ProductId { get; private set; }
}

/// <summary>Which transactions fuelled a note, keyed by the pair: no Id, no TenantId, no stamps.</summary>
public sealed partial class DeliveryNoteTransaction : Entity<(DeliveryNote, Transaction)>
{
    public const Timestamps Stamps = Timestamps.None;

    public decimal Quantity { get; private set; }
}

/// <summary>A context of the app's own, wired by hand, over the tables an existing app keeps.</summary>
public sealed class FuelDbContext(DbContextOptions<FuelDbContext> options) : DbContext(options), ITenantScoped
{
    public const string Schema = "composite_key";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeliveryNote>(b =>
        {
            b.ToTable("DeliveryNotes", Schema);
            b.Property(n => n.Id).ValueGeneratedNever();
            b.Property(n => n.Number).HasMaxLength(32);
        });
        modelBuilder.Entity<Transaction>(b =>
        {
            b.ToTable("Transactions", Schema);
            b.Property(t => t.Id).ValueGeneratedNever();
        });
        modelBuilder.Entity<DeliveryNoteTransaction>(b =>
        {
            b.ToTable("DeliveryNoteTransactions", Schema);
            b.Property(t => t.Quantity).HasPrecision(18, 9);
        });

        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>
/// An entity keyed by several columns on SQL Server: the table's primary key is the pair, and each tenant reads only
/// the rows under its own notes.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerCompositeKeyTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_composite_key";

    public async ValueTask InitializeAsync()
    {
        if (!SqlServer.Available)
        {
            return;
        }

        await using var app = App();
        await using var db = await OpenAsync(app);
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await FuelApp.SeedAsync(db);
    }

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await using var app = App();
            await using var db = await OpenAsync(app);
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public void The_table_is_created_with_the_pair_as_its_primary_key()
    {
        using var db = new FuelDbContext(new DbContextOptionsBuilder<FuelDbContext>()
            .UseSqlServer("Server=(local);Database=rask_model_only;Trusted_Connection=True").Options);

        var script = db.Database.GenerateCreateScript();

        Assert.Contains("PRIMARY KEY ([DeliveryNoteId], [TransactionId])", script, StringComparison.Ordinal);
        Assert.Contains("[Quantity] decimal(18,9) NOT NULL", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_tenant_reads_only_the_rows_under_its_own_notes()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await OpenAsync(app);

        Assert.Equal([(1, 10), (1, 11), (2, 11)], await FuelApp.KeysAsync(db, FuelApp.Acme));
        Assert.Equal([(3, 12)], await FuelApp.KeysAsync(db, FuelApp.Globex));
    }

    private static Task<FuelDbContext> OpenAsync(ServiceProvider app) =>
        app.GetRequiredService<IDbContextFactory<FuelDbContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);

    private static ServiceProvider App() => FuelApp.Build(o => o.UseSqlServer(SqlServer.Database(DatabaseName)));
}

/// <summary>The same on PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresCompositeKeyTests
{
    [Fact]
    public void The_table_is_created_with_the_pair_as_its_primary_key()
    {
        using var db = new FuelDbContext(new DbContextOptionsBuilder<FuelDbContext>()
            .UseNpgsql("Host=localhost;Database=rask_model_only").Options);

        var script = db.Database.GenerateCreateScript();

        Assert.Contains("PRIMARY KEY (\"DeliveryNoteId\", \"TransactionId\")", script, StringComparison.Ordinal);
        Assert.Contains("\"Quantity\" numeric(18,9) NOT NULL", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_tenant_reads_only_the_rows_under_its_own_notes()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var app = FuelApp.Build(o => o.UseRaskPostgresAt(Postgres.Required));
        await using var db = await app.GetRequiredService<IDbContextFactory<FuelDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        await Postgres.ResetSchemaAsync(db, FuelDbContext.Schema);
        await FuelApp.SeedAsync(db);

        Assert.Equal([(1, 10), (1, 11), (2, 11)], await FuelApp.KeysAsync(db, FuelApp.Acme));
        Assert.Equal([(3, 12)], await FuelApp.KeysAsync(db, FuelApp.Globex));
    }
}

/// <summary>Wired the way an application wires it, and the steps both providers take.</summary>
internal static class FuelApp
{
    internal const int Acme = 7;
    internal const int Globex = 8;

    internal static ServiceProvider Build(Action<DbContextOptionsBuilder> provider)
    {
        var services = new ServiceCollection();
        services.AddRaskData();
        services.AddDbContextFactory<FuelDbContext>((sp, o) =>
        {
            provider(o);
            o.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });
        return services.BuildServiceProvider();
    }

    // Written as the existing app writes the table: rows keyed by several columns are read-only through Rask for now.
    internal static async Task SeedAsync(FuelDbContext db)
    {
        var sql = db.GetService<ISqlGenerationHelper>();
        string Table(string name) => sql.DelimitIdentifier(name, FuelDbContext.Schema);
        string Columns(params string[] names) => string.Join(", ", names.Select(sql.DelimitIdentifier));

#pragma warning disable EF1002, EF1003 // Identifiers from the helper, values constant: nothing here comes from outside.
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {Table("DeliveryNotes")} ({Columns("Id", "Number", "TenantId")}) "
            + "VALUES (1, 'A-1', 7), (2, 'A-2', 7), (3, 'G-1', 8);",
            TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {Table("Transactions")} ({Columns("Id", "ProductId", "TenantId")}) "
            + "VALUES (10, 100, 7), (11, 101, 7), (12, 102, 8);",
            TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {Table("DeliveryNoteTransactions")} ({Columns("DeliveryNoteId", "TransactionId", "Quantity")}) "
            + "VALUES (1, 10, 12.5), (1, 11, 3.25), (2, 11, 1), (3, 12, 40);",
            TestContext.Current.CancellationToken);
#pragma warning restore EF1002, EF1003
    }

    internal static async Task<List<(int, int)>> KeysAsync(FuelDbContext db, int tenant)
    {
        using (Tenant.Use(tenant))
        {
            var rows = await db.Set<DeliveryNoteTransaction>().AsNoTracking()
                .OrderBy(t => t.DeliveryNoteId).ThenBy(t => t.TransactionId)
                .Select(t => new { t.DeliveryNoteId, t.TransactionId })
                .ToListAsync(TestContext.Current.CancellationToken);
            return [.. rows.Select(static r => (r.DeliveryNoteId, r.TransactionId))];
        }
    }
}
