using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>A tenant's delivery round: only the root carries the tenant.</summary>
public sealed class Tour : Aggregate<Guid, Timestamps, Versioned, Model, Deletable, PerTenant>
{
    private readonly List<Drop> _drops = [];

    private Tour() { }

    public string Name { get; private set; } = "";

    public IReadOnlyList<Drop> Drops => _drops;

    public static Tour Named(string name, string stop, params string[] packets)
    {
        var round = new Tour { Id = Guid.NewGuid(), Name = name };
        round._drops.Add(Drop.At(stop, packets));
        return round;
    }
}

/// <summary>A child: no tenant, reached through its round.</summary>
public sealed class Drop : Entity<Guid>
{
    private readonly List<Packet> _packets = [];

    private Drop() { }

    public string Town { get; private set; } = "";

    public IReadOnlyList<Packet> Packets => _packets;

    internal void Add(string label) => _packets.Add(Packet.Labelled(label));

    internal static Drop At(string town, string[] packets)
    {
        var stop = new Drop { Id = Guid.NewGuid(), Town = town };
        stop._packets.AddRange(packets.Select(Packet.Labelled));
        return stop;
    }
}

/// <summary>A grandchild: no tenant, reached through its stop and that stop's round.</summary>
public sealed class Packet : Entity<Guid>
{
    private Packet() { }

    public string Label { get; private set; } = "";

    internal static Packet Labelled(string label) => new() { Id = Guid.NewGuid(), Label = label };
}

/// <summary>
/// Only the aggregate root carries the tenant: every entity under it, at any depth, is read through the path the
/// aggregate's own collections make, with nothing declared (#1251).
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class TenancyBelowTheRootTests : IDisposable
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-below-root-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_grandchild_has_no_tenant_column_and_its_filter_walks_up_to_the_root()
    {
        await using var database = await StartDatabaseAsync();

        string sql;
        using (Tenant.Use(Guid.NewGuid()))
        {
            sql = database.Context.Set<Packet>().ToQueryString();
        }

        var parcel = database.Context.Model.FindEntityType(typeof(Packet))!;
        Assert.Null(parcel.FindProperty(Columns.TenantId));
        Assert.Null(database.Context.Model.FindEntityType(typeof(Drop))!.FindProperty(Columns.TenantId));
        Assert.Equal(2, sql.Split("EXISTS").Length - 1);
        Assert.Contains("FROM \"Drop\"", sql, StringComparison.Ordinal);
        Assert.Contains("FROM \"Tour\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_tenant_reads_only_its_own_grandchildren_and_across_reads_all()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Tour.Named("Morning", "Győr", "A-1", "A-2"));
        await SaveAsync(Globex, Tour.Named("Evening", "Graz", "G-1"));

        using (Tenant.Use(Acme))
        {
            Assert.Equal(["A-1", "A-2"], (await Packet.Select(p => p.Label)).Order(StringComparer.Ordinal));
            Assert.Equal(["Győr"], await Drop.Select(s => s.Town));
        }

        using (Tenant.Use(Globex))
        {
            Assert.Equal(["G-1"], await Packet.Select(p => p.Label));
        }

        using (Tenant.Across())
        {
            Assert.Equal(3, await Packet.Count(cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_grandchild_added_under_another_tenants_round_is_refused()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Tour.Named("Morning", "Győr", "A-1"));

        Tour loaded;
        using (Tenant.Use(Acme))
        {
            loaded = await database.Context.Set<Tour>().Include(r => r.Drops).ThenInclude(s => s.Packets)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        loaded.Drops[0].Add("SMUGGLED");
        using (Tenant.Use(Globex))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_child_with_no_way_up_to_its_root_is_a_model_error_naming_it()
    {
        await using var context = new DropsOnlyContext(
            new DbContextOptionsBuilder<DropsOnlyContext>().UseSqlite("Data Source=:memory:").Options);

        var refused = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains("'Drop'", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_child_that_keeps_a_column_takes_its_roots_tenant_even_across_tenants()
    {
        await using var database = await StartDatabaseAsync();
        Waybill waybill;
        using (Tenant.Use(Acme))
        {
            waybill = Waybill.For("W-1", "glass");
            database.Context.Add(waybill);
            await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        waybill.Add("steel");
        using (Tenant.Across())
        {
            await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var steel = waybill.Lines.Single(l => l.Goods == "steel");
        Assert.Equal((long)Acme, database.Context.Entry(steel).Property(Columns.TenantId).CurrentValue);
    }

    [Fact]
    public async Task A_child_whose_column_disagrees_with_its_root_is_refused()
    {
        await using var database = await StartDatabaseAsync();
        using (Tenant.Use(Acme))
        {
            var waybill = Waybill.For("W-1", "glass");
            database.Context.Add(waybill);
            await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            database.Context.Entry(waybill.Lines.Single()).Property(Columns.TenantId).CurrentValue = (long)Globex;
        }

        using (Tenant.Across())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
    }

    private static async Task SaveAsync(int tenant, Tour round)
    {
        using (Tenant.Use(tenant))
        {
            await round.Save(cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));

    // A context that maps a child and not the aggregate it is part of: there is no path to a tenant.
    private sealed class DropsOnlyContext(DbContextOptions<DropsOnlyContext> options) : DbContext(options), ITenantScoped
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Drop>().Ignore(s => s.Packets);
            modelBuilder.ApplyRaskConventions(this);
        }
    }
}
