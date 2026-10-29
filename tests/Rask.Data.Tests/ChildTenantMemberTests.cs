using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests.ChildTenants;

/// <summary>A numbered tenant's supply: the root carries the tenant.</summary>
public sealed class Supply : Aggregate<int, Model, Deletable, PerTenant<int>>
{
    private readonly List<SupplyLine> _lines = [];

    private Supply() { }

    public string Reference { get; private set; } = "";

    public IReadOnlyList<SupplyLine> Lines => _lines;

    public static Supply Of(string reference, params string[] goods)
    {
        var supply = new Supply { Reference = reference };
        supply._lines.AddRange(goods.Select(SupplyLine.Of));
        return supply;
    }

    public void Add(string goods) => _lines.Add(SupplyLine.Of(goods));
}

/// <summary>A child keeping a legacy tenant column behind an INTERNAL member, NULL on old rows.</summary>
public sealed class SupplyLine : Entity<int>
{
    private SupplyLine() { }

    internal int? TenantId { get; private set; }

    public string Goods { get; private set; } = "";

    // The legacy table's column, mapped as the app maps it: EF Core's convention skips an internal member.
    public static void Configure(EntityTypeBuilder<SupplyLine> builder) => builder.Property(l => l.TenantId);

    internal static SupplyLine Of(string goods) => new() { Goods = goods };

    internal void Rename(string goods) => Goods = goods;
}

/// <summary>A Guid tenant's hopper: the root carries the tenant.</summary>
public sealed class Hopper : Aggregate<Guid, Model, Deletable, PerTenant>
{
    private readonly List<HopperBin> _bins = [];

    private Hopper() { }

    public string Name { get; private set; } = "";

    public IReadOnlyList<HopperBin> Bins => _bins;

    public static Hopper Of(string name, params string[] bins)
    {
        var hopper = new Hopper { Id = Guid.CreateVersion7(), Name = name };
        hopper._bins.AddRange(bins.Select(HopperBin.Of));
        return hopper;
    }
}

/// <summary>A child keeping a tenant column behind a PUBLIC member, NULL on old rows.</summary>
public sealed class HopperBin : Entity<Guid>
{
    private HopperBin() { }

    public Guid? TenantId { get; private set; }

    public string Label { get; private set; } = "";

    internal static HopperBin Of(string label) => new() { Id = Guid.CreateVersion7(), Label = label };
}

/// <summary>
/// A child is partitioned THROUGH its root: a <c>TenantId</c> member on it — internal or public, int or Guid — is
/// a column it keeps, never a filter of its own. Only a type that names <see cref="PerTenant" /> itself — an
/// aggregate root, the one base that takes the trait — is filtered by its own column.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ChildTenantMemberTests : IDisposable
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-child-tenant-member-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_child_with_an_internal_int_tenant_member_reads_its_null_tenant_rows_through_its_root()
    {
        await using var database = await StartDatabaseAsync();
        var supply = await SaveAsync(Acme, Supply.Of("S-1", "nails", "screws", "bolts"));
        await ForgetTenantAsync(database, "SupplyLine", "Goods", "screws", "bolts");

        using (Tenant.Use(Acme))
        {
            Assert.Equal(
                ["bolts", "nails", "screws"],
                (await SupplyLine.Select(l => l.Goods)).Order(StringComparer.Ordinal));
            var loaded = (await database.Load<Supply>(supply.Id, TestContext.Current.CancellationToken))!;
            Assert.Equal(3, loaded.Lines.Count);
        }
    }

    [Fact]
    public async Task A_child_with_a_public_guid_tenant_member_reads_its_null_tenant_rows_through_its_root()
    {
        await using var database = await StartDatabaseAsync();
        var acme = Guid.CreateVersion7();
        var hopper = await SaveAsync(acme, Hopper.Of("H-1", "red", "green", "blue"));
        await ForgetTenantAsync(database, "HopperBin", "Label", "green", "blue");

        using (Tenant.Use(acme))
        {
            Assert.Equal(
                ["blue", "green", "red"],
                (await HopperBin.Select(b => b.Label)).Order(StringComparer.Ordinal));
            var loaded = (await database.Load<Hopper>(hopper.Id, TestContext.Current.CancellationToken))!;
            Assert.Equal(3, loaded.Bins.Count);
        }
    }

    [Fact]
    public async Task Another_tenants_children_stay_invisible_null_tenant_or_not()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Supply.Of("S-A", "nails", "screws"));
        var theirs = await SaveAsync(Globex, Supply.Of("S-G", "rivets", "washers"));
        await ForgetTenantAsync(database, "SupplyLine", "Goods", "screws", "washers");

        using (Tenant.Use(Acme))
        {
            Assert.Equal(["nails", "screws"], (await SupplyLine.Select(l => l.Goods)).Order(StringComparer.Ordinal));
            Assert.Equal(2, await SupplyLine.Count(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Null(await database.Load<Supply>(theirs.Id, TestContext.Current.CancellationToken));
            Assert.Equal(
                ["nails", "screws"],
                (await database.Context.Set<SupplyLine>().Select(l => l.Goods).ToListAsync(TestContext.Current.CancellationToken))
                .Order(StringComparer.Ordinal));
        }

        using (Tenant.Use(Globex))
        {
            Assert.Equal(["rivets", "washers"], (await SupplyLine.Select(l => l.Goods)).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task Another_guid_tenants_public_member_children_stay_invisible()
    {
        await using var database = await StartDatabaseAsync();
        var acme = Guid.CreateVersion7();
        var globex = Guid.CreateVersion7();
        await SaveAsync(acme, Hopper.Of("H-A", "red"));
        await SaveAsync(globex, Hopper.Of("H-G", "black", "white"));
        await ForgetTenantAsync(database, "HopperBin", "Label", "white");

        using (Tenant.Use(acme))
        {
            Assert.Equal(["red"], await HopperBin.Select(b => b.Label));
        }
    }

    [Fact]
    public async Task A_child_with_a_tenant_member_is_filtered_through_its_parent_not_its_column()
    {
        await using var database = await StartDatabaseAsync();

        string write;
        using (Tenant.Use(Acme))
        {
            write = database.Context.Set<SupplyLine>().ToQueryString();
        }

        await using var read = database.OpenRead();
        var face = read.Model.GetEntityTypes().Single(t => t.GetTableName() == "SupplyLine");
        Assert.Contains("EXISTS", write, StringComparison.Ordinal);
        Assert.Contains("FROM \"Supply\"", write, StringComparison.Ordinal);
        Assert.Equal(true, face.FindAnnotation("Rask:TenantThroughParent")?.Value);
    }

    [Fact]
    public async Task A_child_that_keeps_a_tenant_member_is_still_stamped_with_its_roots_tenant()
    {
        await using var database = await StartDatabaseAsync();

        var supply = await SaveAsync(Acme, Supply.Of("S-1", "nails"));

        Assert.Equal(Acme, supply.Lines.Single().TenantId);
    }

    [Fact]
    public async Task A_child_line_added_under_another_tenants_supply_is_refused()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Supply.Of("S-1", "nails"));
        await ForgetTenantAsync(database, "SupplyLine", "Goods", "nails");

        Supply loaded;
        using (Tenant.Use(Acme))
        {
            loaded = await database.Context.Set<Supply>().Include(s => s.Lines)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        loaded.Add("smuggled");
        using (Tenant.Use(Globex))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task The_type_that_names_per_tenant_keeps_a_filter_of_its_own_and_its_null_rows_belong_to_nobody()
    {
        await using var database = await StartDatabaseAsync();
        var acme = Guid.CreateVersion7();
        await SaveAsync(acme, Hopper.Of("H-1", "red"));
        await SaveAsync(acme, Hopper.Of("H-2", "black"));
        await ForgetTenantAsync(database, "Hopper", "Name", "H-2");

        string sql;
        using (Tenant.Use(acme))
        {
            sql = database.Context.Set<Hopper>().ToQueryString();
            Assert.Equal(["H-1"], await Hopper.Select(h => h.Name));
            Assert.Equal(["red"], await HopperBin.Select(b => b.Label));
        }

        Assert.DoesNotContain("EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("\"TenantId\" = @", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_legacy_child_whose_column_is_null_can_still_be_edited_under_its_roots_tenant()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Supply.Of("S-1", "nails", "screws"));
        await ForgetTenantAsync(database, "SupplyLine", "Goods", "screws");

        using (Tenant.Use(Acme))
        {
            var loaded = await database.Context.Set<Supply>().Include(s => s.Lines)
                .SingleAsync(TestContext.Current.CancellationToken);
            loaded.Lines.Single(l => l.Goods == "screws").Rename("bolts");
            await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

            Assert.Equal(["bolts", "nails"], (await SupplyLine.Select(l => l.Goods)).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task A_legacy_child_loaded_in_one_tenant_is_refused_when_saved_in_another()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Supply.Of("S-1", "screws"));
        await ForgetTenantAsync(database, "SupplyLine", "Goods", "screws");

        Supply loaded;
        using (Tenant.Use(Acme))
        {
            loaded = await database.Context.Set<Supply>().Include(s => s.Lines)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        loaded.Lines.Single().Rename("stolen");
        using (Tenant.Use(Globex))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
    }

    private static async Task<T> SaveAsync<T>(int tenant, T aggregate)
        where T : class, IAggregate
    {
        using (Tenant.Use(tenant))
        {
            return await GeneratedModelWrites.Create(aggregate, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private static async Task<T> SaveAsync<T>(Guid tenant, T aggregate)
        where T : class, IAggregate
    {
        using (Tenant.Use(tenant))
        {
            return await GeneratedModelWrites.Create(aggregate, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    // A legacy row: the child's own tenant column was never filled, the parent's is.
    private static async Task ForgetTenantAsync(TestDatabase database, string table, string column, params string[] values)
    {
        var list = string.Join(", ", values.Select(static v => "'" + v + "'"));
#pragma warning disable EF1002 // Test-only names and literals, none from input.
        var changed = await database.Context.Database.ExecuteSqlRawAsync(
            $"UPDATE \"{table}\" SET \"TenantId\" = NULL WHERE \"{column}\" IN ({list});",
            TestContext.Current.CancellationToken);
#pragma warning restore EF1002

        Assert.Equal(values.Length, changed);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
