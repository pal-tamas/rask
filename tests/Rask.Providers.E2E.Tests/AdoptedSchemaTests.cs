using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Providers.E2E.Tests;

/// <summary>
/// A table that already exists, mapped as it is: <c>Destinations (Id int IDENTITY, Name nvarchar(255) NOT NULL,
/// TenantId int NULL)</c> with a unique index over <c>(Name, TenantId)</c>, and none of Rask's own columns.
/// </summary>
public sealed class Destination : Aggregate<int>
{
    public const string NameTaken = "Ilyen néven már létezik viszonylat.";

    private Destination() { }

    public const Timestamps Stamps = Timestamps.None;

    public const Concurrency Checks = Concurrency.None;

    public const Tenancy Scope = Tenancy.PerTenant;

    public int? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public static Destination Named(string name) => new() { Name = name };

    public void Rename(string name) => Name = name;

    public static void Configure(EntityTypeBuilder<Destination> builder)
    {
        builder.ToTable("Destinations");
        builder.Property(d => d.Name).IsRequired().HasMaxLength(255);
        builder.HasIndex(d => new { d.Name, d.TenantId }).IsUnique().HasViolationMessage(NameTaken);
    }
}

/// <summary>The context an adopting app adds beside its own: Rask's model over the tables that are there.</summary>
public sealed class AdoptedDomainContext(DbContextOptions<AdoptedDomainContext> options) : RaskDbContext(options);

/// <summary>What the adopting app already had, for the test that has to build such a database first.</summary>
public sealed class LegacyTenant
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public sealed class LegacyDestination
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int? TenantId { get; set; }

    public LegacyTenant? Tenant { get; set; }
}

public sealed class AdoptedLegacyContext(DbContextOptions<AdoptedLegacyContext> options) : DbContext(options)
{
    public const string DatabaseName = "rask_adopted";

    public DbSet<LegacyTenant> Tenants => Set<LegacyTenant>();

    public DbSet<LegacyDestination> Destinations => Set<LegacyDestination>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LegacyTenant>().ToTable("Tenants").Property(t => t.Name).HasMaxLength(255);

        modelBuilder.Entity<LegacyDestination>(b =>
        {
            b.ToTable("Destinations");
            b.Property(d => d.Name).IsRequired().HasMaxLength(255);
            b.HasOne(d => d.Tenant).WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(d => new { d.Name, d.TenantId }).IsUnique().HasFilter("[TenantId] IS NOT NULL");
        });
    }
}

/// <summary>
/// The whole of adopting one existing table, against a real SQL Server: read, insert, rename, collide, delete
/// and the request with no tenant — for two tenants read from the database's own <c>Tenants</c> table.
/// </summary>
/// <remarks>
/// <para>
/// It creates no table, column or index and runs no migration: the catalogue is read before and after and must
/// be identical, and so must the number of rows — every row it inserts is named <c>zz-raskdata-…</c> and is
/// deleted in a <c>finally</c>.
/// </para>
/// <para>
/// The provider is plain <c>UseSqlServer</c>, as an app that keeps its own connection string writes it.
/// </para>
/// </remarks>
internal static class AdoptedSchemaScenario
{
    internal static async Task RunAsync(string connectionString, Action<string> log)
    {
        var tenant = new ResolvedHere();
        var services = new ServiceCollection();
        services.AddSingleton(tenant);
        services.AddRaskCqrs();
        services.AddRaskData<AdoptedDomainContext>();
        services.AddDbContextFactory<AdoptedDomainContext>((sp, o) => o
            .UseSqlServer(connectionString)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlServer(connectionString));
        services.AddRaskTenant(sp => sp.GetRequiredService<ResolvedHere>().TenantId);

        await using var app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(app);

        var catalogueBefore = await CatalogueAsync(connectionString);
        var rowsBefore = await ScalarAsync(connectionString, "SELECT COUNT(*) FROM dbo.Destinations");
        // The two tenants that own the most rows, so the reads below compare against real data where there is any.
        var tenants = await IdsAsync(
            connectionString,
            "SELECT TOP 2 t.Id FROM dbo.Tenants t ORDER BY (SELECT COUNT(*) FROM dbo.Destinations d WHERE d.TenantId = t.Id) DESC, t.Id");
        Assert.Equal(2, tenants.Count);
        var (first, second) = (tenants[0], tenants[1]);
        var name = $"zz-raskdata-{Guid.NewGuid():N}";
        log($"tenants {first} and {second}; {rowsBefore} row(s) in Destinations before; scratch name {name}");

        try
        {
            foreach (var id in tenants)
            {
                await ReadsOnlyItsOwnRowsAsync(app, tenant, connectionString, id, log);
            }

            var saved = await As(app, tenant, first, async () =>
            {
                var destination = Destination.Named(name);
                await destination.Save();
                return destination;
            });
            var stamped = await ScalarAsync(connectionString, "SELECT TenantId FROM dbo.Destinations WHERE Id = @p", saved.Id);
            Assert.Equal(first, stamped);
            Assert.Equal(first, saved.TenantId);
            log($"insert: row {saved.Id} stamped TenantId = {stamped}");

            var renamed = name + "-renamed";
            await As(app, tenant, first, () => Destination.Update(saved.Id, d => d.Rename(renamed)));
            Assert.Equal(1, await ScalarAsync(connectionString, "SELECT COUNT(*) FROM dbo.Destinations WHERE Id = @p AND Name = @q", saved.Id, renamed));
            log("rename: the row holds the new name");

            var collision = await Assert.ThrowsAsync<RaskValidationException>(
                () => As(app, tenant, first, () => Destination.Named(renamed).Save()));
            Assert.Equal([Destination.NameTaken], collision.Errors["Name"]);
            Assert.IsType<SqlException>(collision.InnerException!.InnerException);
            log($"collision in tenant {first}: \"{collision.Errors["Name"][0]}\" (SQL Server error {((SqlException)collision.InnerException.InnerException!).Number})");

            var theirs = await As(app, tenant, second, async () =>
            {
                var destination = Destination.Named(renamed);
                await destination.Save();
                return destination;
            });
            Assert.Equal(second, theirs.TenantId);
            Assert.Null(await As(app, tenant, second, () => Destination.Find(saved.Id)));
            log($"tenant {second} holds the same name as row {theirs.Id}, and cannot find row {saved.Id}");

            // Again, now that BOTH tenants hold a row the other must not see.
            foreach (var id in tenants)
            {
                await ReadsOnlyItsOwnRowsAsync(app, tenant, connectionString, id, log);
            }

            var sql = await As(app, tenant, first, () =>
            {
                using var context = app.GetRequiredService<IDbContextFactory<AdoptedDomainContext>>().CreateDbContext();
                return Task.FromResult(context.Set<Destination>().ToQueryString());
            });
            Assert.Contains($" int = {first};", sql, StringComparison.Ordinal);
            Assert.Contains("[d].[TenantId] = @", sql, StringComparison.Ordinal);
            log("filter sql: " + sql.ReplaceLineEndings(" "));

            await As(app, tenant, first, () => Destination.Delete(saved.Id));
            await As(app, tenant, second, () => Destination.Delete(theirs.Id));
            Assert.Equal(0, await ScalarAsync(connectionString, "SELECT COUNT(*) FROM dbo.Destinations WHERE Id IN (@p, @q)", saved.Id, theirs.Id));
            log("delete: both rows are gone");

            Assert.Equal(0, await As(app, tenant, null, () => Destination.Count()));
            await Assert.ThrowsAsync<MissingTenantException>(() => As(app, tenant, null, () => Destination.Named(name).Save()));
            log("no tenant: Count() = 0 and Save() is refused with MissingTenantException");
        }
        finally
        {
            Db.Reset();
            await ExecuteAsync(connectionString, "DELETE FROM dbo.Destinations WHERE Name LIKE @p", name + "%");
        }

        var rowsAfter = await ScalarAsync(connectionString, "SELECT COUNT(*) FROM dbo.Destinations");
        Assert.Equal(rowsBefore, rowsAfter);
        Assert.Equal(catalogueBefore, await CatalogueAsync(connectionString));
        log($"{rowsAfter} row(s) in Destinations after; tables, columns and indexes unchanged");
    }

    private static async Task ReadsOnlyItsOwnRowsAsync(
        ServiceProvider app, ResolvedHere tenant, string connectionString, int id, Action<string> log)
    {
        var expected = await IdsAsync(connectionString, "SELECT Id FROM dbo.Destinations WHERE TenantId = @p ORDER BY Id", id);

        var count = await As(app, tenant, id, () => Destination.Count());
        var read = await As(app, tenant, id, async () => await Destination.OrderBy(d => d.Id).Select(d => d.Id));

        Assert.Equal(expected.Count, count);
        Assert.Equal(expected, read);
        log($"tenant {id}: Rask reads {count} row(s), plain SQL counts {expected.Count}");
    }

    // One request's worth of work: the resolver is asked once per scope, so each step gets a scope of its own.
    private static async Task<T> As<T>(ServiceProvider app, ResolvedHere tenant, int? id, Func<Task<T>> work)
    {
        tenant.TenantId = id;
        await using var request = app.CreateAsyncScope();
        using (Db.UseScope(request.ServiceProvider))
        {
            return await work();
        }
    }

    private static Task As(ServiceProvider app, ResolvedHere tenant, int? id, Func<Task> work) =>
        As(app, tenant, id, async () =>
        {
            await work();
            return 0;
        });

    // Every table, column and index of the database, as text: creating anything changes it.
    private static async Task<string> CatalogueAsync(string connectionString)
    {
        const string Sql = """
            SELECT 'column ' + TABLE_SCHEMA + '.' + TABLE_NAME + '.' + COLUMN_NAME + ' ' + DATA_TYPE + ' ' + IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            UNION ALL
            SELECT 'index ' + OBJECT_NAME(object_id) + '.' + ISNULL(name, '(heap)') + ' ' + CAST(is_unique AS varchar(1))
            FROM sys.indexes WHERE OBJECTPROPERTY(object_id, 'IsUserTable') = 1
            ORDER BY 1
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(Sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var lines = new List<string>();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join('\n', lines);
    }

    private static async Task<List<int>> IdsAsync(string connectionString, string sql, params object[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();

        var ids = new List<int>();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }

    private static async Task<int?> ScalarAsync(string connectionString, string sql, params object[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteScalarAsync() is int value ? value : null;
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params object[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    // Parameters are @p then @q, which is all any statement here needs.
    private static SqlCommand Command(SqlConnection connection, string sql, object[] parameters)
    {
        var command = new SqlCommand(sql, connection);
        for (var i = 0; i < parameters.Length; i++)
        {
            command.Parameters.AddWithValue(i == 0 ? "@p" : "@q", parameters[i]);
        }

        return command;
    }

    private sealed class ResolvedHere
    {
        public int? TenantId { get; set; }
    }
}

/// <summary>
/// The scenario against a database this test builds the way an app that predates Rask would have: a legacy
/// context creates <c>Tenants</c> and <c>Destinations</c>, and Rask's context is only ever pointed at them.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerAdoptedSchemaTests : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        if (!SqlServer.Available)
        {
            return;
        }

        await using var legacy = NewLegacy();
        await legacy.Database.EnsureDeletedAsync();
        await legacy.Database.EnsureCreatedAsync();

        var (acme, globex) = (new LegacyTenant { Name = "Acme" }, new LegacyTenant { Name = "Globex" });
        legacy.AddRange(
            new LegacyDestination { Name = "Budapest", Tenant = acme },
            new LegacyDestination { Name = "Szeged", Tenant = acme },
            new LegacyDestination { Name = "Vienna", Tenant = globex },
            new LegacyDestination { Name = "Nowhere" });
        await legacy.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await using var legacy = NewLegacy();
            await legacy.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task An_existing_table_is_read_written_and_left_as_it_was_found()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);

        await AdoptedSchemaScenario.RunAsync(
            SqlServer.Database(AdoptedLegacyContext.DatabaseName), TestContext.Current.TestOutputHelper!.WriteLine);
    }

    private static AdoptedLegacyContext NewLegacy() =>
        new(new DbContextOptionsBuilder<AdoptedLegacyContext>()
            .UseSqlServer(SqlServer.Database(AdoptedLegacyContext.DatabaseName)).Options);
}

/// <summary>
/// The same scenario against a database that ALREADY EXISTS and that this test did not build — a copy of the
/// schema an app is being moved off, with whatever data it holds.
/// </summary>
/// <remarks>
/// Set <c>RASK_MSSQL_EXISTING_DB</c> to the whole connection string of a database that has
/// <c>dbo.Tenants (Id int)</c> with at least two rows and <c>dbo.Destinations (Id int IDENTITY, Name
/// nvarchar(255) NOT NULL, TenantId int NULL)</c> with the unique index <c>IX_Destinations_Name_TenantId</c>.
/// Nothing is created, altered or migrated, and every row the test inserts is deleted again.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerExistingDatabaseTests
{
    private const string SkipReason =
        "Needs a database that already has Tenants and Destinations: set RASK_MSSQL_EXISTING_DB to its connection string.";

    private static string? ConnectionString => Environment.GetEnvironmentVariable("RASK_MSSQL_EXISTING_DB");

    [Fact]
    public async Task A_table_somebody_else_created_is_read_written_and_left_as_it_was_found()
    {
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(ConnectionString), SkipReason);

        await AdoptedSchemaScenario.RunAsync(ConnectionString!, TestContext.Current.TestOutputHelper!.WriteLine);
    }
}
