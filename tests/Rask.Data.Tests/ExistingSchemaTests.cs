using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

/// <summary>The class an existing app already has for the table: public setters, mapped by its own context.</summary>
public sealed class LegacyDestination
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int? TenantId { get; set; }
}

/// <summary>The context the app already has. It owns the schema, and knows nothing about Rask.</summary>
public sealed class LegacyContext(DbContextOptions<LegacyContext> options) : DbContext(options)
{
    public DbSet<LegacyDestination> Destinations => Set<LegacyDestination>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<LegacyDestination>(b =>
        {
            b.ToTable("Destinations");
            b.Property(d => d.Name).IsRequired().HasMaxLength(255);
            b.HasIndex(d => d.TenantId);
            b.HasIndex(d => new { d.Name, d.TenantId }).IsUnique();
        });
}

/// <summary>The second context the app adds: Rask's model, over the tables that are already there.</summary>
public sealed class DomainContext(DbContextOptions<DomainContext> options) : RaskDbContext(options);

/// <summary>
/// An existing app models its domain with Rask aggregates over the tables it already has: its own context
/// keeps the schema and its own classes, a second context deriving from <see cref="RaskDbContext" /> maps the
/// same tables, and one line says where the tenant comes from.
/// </summary>
/// <remarks>
/// Wired the way <c>docs/data.md</c> tells an app to — through the container — so the test is the documentation
/// compiled. Every row the Rask side writes is read back through the LEGACY context, and the other way round.
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class ExistingSchemaTests : IAsyncLifetime
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-existing-schema-{Guid.NewGuid():N}.db");
    private ServiceProvider _app = null!;

    public async ValueTask InitializeAsync()
    {
        var connection = $"Data Source={_dbPath};Pooling=False";
        var services = new ServiceCollection();

        // What the app had before.
        services.AddDbContextFactory<LegacyContext>(o => o.UseSqlite(connection));
        services.AddScoped<RequestStub>();

        // What it adds.
        services.AddRaskCqrs();
        services.AddRaskData<DomainContext>();
        services.AddDbContextFactory<DomainContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite(connection));
        services.AddRaskTenant(sp => sp.GetRequiredService<RequestStub>().TenantId);

        _app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(_app);

        // The legacy context owns the schema. Rask's is never asked to create or migrate anything.
        await using var legacy = await OpenLegacyAsync();
        await legacy.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _app.DisposeAsync();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task A_save_through_rask_is_a_row_the_legacy_context_reads_with_its_tenant()
    {
        await using var request = RequestFor(Acme);

        using (Db.UseScope(request.ServiceProvider))
        {
            await Destination.Named("Budapest").Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        await using var legacy = await OpenLegacyAsync();
        var row = await legacy.Destinations.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(("Budapest", Acme), (row.Name, row.TenantId));
    }

    [Fact]
    public async Task A_read_through_rask_returns_only_the_rows_the_legacy_context_wrote_for_this_tenant()
    {
        await LegacyInsertAsync(("Budapest", Acme), ("Szeged", Acme), ("Vienna", Globex), ("Nowhere", null));
        await using var request = RequestFor(Acme);

        List<string> names;
        using (Db.UseScope(request.ServiceProvider))
        {
            names = await Destination.OrderBy(d => d.Name).Take(15).Select(d => d.Name);
        }

        Assert.Equal(["Budapest", "Szeged"], names);
    }

    [Fact]
    public async Task An_update_through_rask_changes_the_row_the_legacy_context_wrote()
    {
        await LegacyInsertAsync(("Budapest", Acme), ("Vienna", Globex));
        await using var request = RequestFor(Acme);

        using (Db.UseScope(request.ServiceProvider))
        {
            var id = (await Destination.First(d => d.Name == "Budapest", TestContext.Current.CancellationToken))!.Id;
            await Destination.Update(id, d => d.Rename("Pest"), cancellationToken: TestContext.Current.CancellationToken);
        }

        await using var legacy = await OpenLegacyAsync();
        Assert.Equal(
            [("Pest", Acme), ("Vienna", Globex)],
            (await legacy.Destinations.OrderBy(d => d.Name).ToListAsync(TestContext.Current.CancellationToken))
            .Select(d => (d.Name, d.TenantId!.Value)));
    }

    [Fact]
    public async Task A_request_for_one_tenant_cannot_change_or_delete_a_row_the_legacy_context_wrote_for_another()
    {
        await LegacyInsertAsync(("Budapest", Acme));
        await using var legacy = await OpenLegacyAsync();
        var theirs = (await legacy.Destinations.SingleAsync(TestContext.Current.CancellationToken)).Id;
        await using var request = RequestFor(Globex);

        using (Db.UseScope(request.ServiceProvider))
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => Destination.Update(
                theirs, d => d.Rename("taken"), cancellationToken: TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => Destination.Delete(theirs, cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Equal("Budapest", (await legacy.Destinations.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).Name);
    }

    [Fact]
    public async Task A_request_with_no_tenant_reads_nothing_and_writes_nothing()
    {
        await LegacyInsertAsync(("Budapest", Acme), ("Nowhere", null));
        await using var request = RequestFor(tenant: null);

        using (Db.UseScope(request.ServiceProvider))
        {
            Assert.Equal(0, await Destination.Count(cancellationToken: TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<MissingTenantException>(
                () => Destination.Named("Graz").Save(cancellationToken: TestContext.Current.CancellationToken));
        }

        await using var legacy = await OpenLegacyAsync();
        Assert.Equal(2, await legacy.Destinations.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_tables_own_unique_index_refuses_a_name_the_tenant_already_has()
    {
        await LegacyInsertAsync(("Budapest", Acme), ("Vienna", Globex));
        await using var request = RequestFor(Acme);

        using (Db.UseScope(request.ServiceProvider))
        {
            await Destination.Named("Vienna").Save(cancellationToken: TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => Destination.Named("Budapest").Save(cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    private AsyncServiceScope RequestFor(int? tenant)
    {
        var request = _app.CreateAsyncScope();
        request.ServiceProvider.GetRequiredService<RequestStub>().TenantId = tenant;
        return request;
    }

    private Task<LegacyContext> OpenLegacyAsync() =>
        _app.GetRequiredService<IDbContextFactory<LegacyContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);

    private async Task LegacyInsertAsync(params (string Name, int? Tenant)[] rows)
    {
        await using var legacy = await OpenLegacyAsync();
        legacy.AddRange(rows.Select(r => new LegacyDestination { Name = r.Name, TenantId = r.Tenant }));
        await legacy.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed class RequestStub
    {
        public int? TenantId { get; set; }
    }
}
