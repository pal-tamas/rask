using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A tenant's port.</summary>
public sealed class ThroughPort : Aggregate<Guid, PerTenant>
{
    private ThroughPort() { }

    public string Name { get; private set; } = "";

    public static ThroughPort Named(string name) => new() { Id = Guid.NewGuid(), Name = name };
}

/// <summary>A ticket with no tenant of its own: it is the tenant's whose port it names.</summary>
public sealed class ThroughTicket : Aggregate<Guid, PerTenantThrough<ThroughPort>>
{
    private ThroughTicket() { }

    public Guid ThroughPortId { get; private set; }

    public string Note { get; private set; } = "";

    public static ThroughTicket In(Guid port, string note) => new() { Id = Guid.NewGuid(), ThroughPortId = port, Note = note };

    public static void Configure(EntityTypeBuilder<ThroughTicket> builder) =>
        builder.HasOne<ThroughPort>().WithMany().HasForeignKey(s => s.ThroughPortId);
}

/// <summary>
/// F-31: <see cref="PerTenantThrough{TParent}" /> partitions an aggregate through the one it points at, and
/// <c>X.Across(reason)</c> lifts the tenant filter for one read — and only the tenant filter.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class TenantThroughTests : IDisposable
{
    private static readonly Guid Acme = new("5d0f7a7e-1b0c-4a3e-9b1a-0000000000a1");
    private static readonly Guid Globex = new("5d0f7a7e-1b0c-4a3e-9b1a-0000000000b2");

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-tenant-through-{Guid.NewGuid():N}.db");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task An_aggregate_through_another_has_no_tenant_column_and_a_filter_through_it()
    {
        await using var database = await StartDatabaseAsync();

        var ticket = database.Context.Model.FindEntityType(typeof(ThroughTicket))!;

        Assert.Null(ticket.FindProperty(Columns.TenantId));
        Assert.Contains(ticket.GetDeclaredQueryFilters(), f => f.Key == "Tenant");
    }

    [Fact]
    public async Task Each_tenant_reads_the_rows_under_its_own_parents_only()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        using (Tenant.Use(Acme))
        {
            Assert.Equal(["acme ticket"], await ThroughTicket.Select(s => s.Note));
            Assert.Equal(["acme ticket"], await database.Context.Set<ThroughTicket>().Select(s => s.Note).ToListAsync(Token));
        }
    }

    [Fact]
    public async Task Across_with_a_reason_reads_every_tenants_rows_for_that_query_alone()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        using (Tenant.Use(Acme))
        {
            Assert.Equal(["acme ticket", "globex ticket"], (await ThroughTicket.Across("every port's berths").Select(s => s.Note)).Order(StringComparer.Ordinal));
            Assert.Equal(2, await ThroughPort.Across("every port").Count(cancellationToken: Token));
            Assert.Equal(1, await ThroughPort.Count(cancellationToken: Token));
        }
    }

    [Fact]
    public async Task Across_asks_no_parent_for_the_tenant()
    {
        await using var database = await StartDatabaseAsync();

        var sql = ThroughTicket.Across("audit").AsQueryable().ToQueryString();

        Assert.DoesNotContain("EXISTS", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Across_refuses_an_empty_reason()
    {
        await using var database = await StartDatabaseAsync();

        Assert.Throws<ArgumentException>(() => ThroughTicket.Across(" "));
    }

    [Fact]
    public async Task A_berth_saved_under_another_tenants_wharf_is_refused()
    {
        await using var database = await StartDatabaseAsync();
        var (_, globexWharf) = await SeedAsync();

        using (Tenant.Use(Acme))
        {
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => ThroughTicket.In(globexWharf, "smuggled").Save(cancellationToken: Token));

            Assert.Contains("'ThroughPort'", refused.Message, StringComparison.Ordinal);
        }
    }

    private static async Task<(Guid Acme, Guid Globex)> SeedAsync()
    {
        var ids = new List<Guid>();
        foreach (var (tenant, name) in new[] { (Acme, "acme"), (Globex, "globex") })
        {
            using (Tenant.Use(tenant))
            {
                var port = ThroughPort.Named(name);
                await port.Save(cancellationToken: Token);
                await ThroughTicket.In(port.Id, name + " ticket").Save(cancellationToken: Token);
                ids.Add(port.Id);
            }
        }

        return (ids[0], ids[1]);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
