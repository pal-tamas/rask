using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A tenant's driver, as an existing app has it: a text key and a <see cref="Guid" /> tenant kept as text.</summary>
public sealed class Driver : Aggregate<string>, IFormModel, IDeletable, IPerTenant
{
    private readonly List<Chip> _chips = [];

    private Driver() { }

    public Guid? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public IReadOnlyList<Chip> Chips => _chips;

    public static Driver Named(string key, string name, params string[] chips)
    {
        var driver = new Driver { Id = key, Name = name };
        driver._chips.AddRange(chips.Select(Chip.Coded));
        return driver;
    }

    public void Hand(string chip) => _chips.Add(Chip.Coded(chip));

    public void TakeBack(string chip) => _chips.RemoveAll(c => c.Code == chip);

    public static void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("Driver");
        builder.Property(d => d.TenantId).HasConversion<string>().HasMaxLength(64);
    }
}

/// <summary>The other parent of the same chips: a chip is a driver's or a vehicle's, never both.</summary>
public sealed class Vehicle : Aggregate<string>, IFormModel, IDeletable, IPerTenant
{
    private readonly List<Chip> _chips = [];

    private Vehicle() { }

    public Guid? TenantId { get; private set; }

    public string Plate { get; private set; } = "";

    public IReadOnlyList<Chip> Chips => _chips;

    public static Vehicle Plated(string key, string plate, params string[] chips)
    {
        var vehicle = new Vehicle { Id = key, Plate = plate };
        vehicle._chips.AddRange(chips.Select(Chip.Coded));
        return vehicle;
    }

    public static void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicle");
        builder.Property(v => v.TenantId).HasConversion<string>().HasMaxLength(64);
    }
}

/// <summary>
/// One table under two roots, with NO tenant column: it belongs to a tenant through whichever parent it has.
/// </summary>
public sealed class Chip : Entity<string>
{
    public const string Taken = "This chip is already handed out.";

    private Chip() { }

    public string Code { get; private set; } = "";

    public string? DriverId { get; private set; }

    public string? VehicleId { get; private set; }

    internal static Chip Coded(string code) => new() { Id = Guid.NewGuid().ToString("N"), Code = code };

    // A row made outside its aggregate, the way a tool or a careless handler would: only the key says whose it is.
    internal static Chip Under(string driver, string code) =>
        new() { Id = Guid.NewGuid().ToString("N"), Code = code, DriverId = driver };

    internal void MoveTo(string driver) => DriverId = driver;

    public static void Configure(EntityTypeBuilder<Chip> builder)
    {
        builder.ToTable("Chip");
        builder.HasIndex(c => c.Code).HasDatabaseName("Chip_code_key").IsUnique(Taken);
    }
}

/// <summary>
/// A child whose table has no tenant column is partitioned through its parent (#1251): every read asks the parent,
/// Across lifts it, and a write lands only under a parent of the tenant in flight.
/// </summary>
/// <remarks>
/// Each test that proves isolation writes rows for TWO tenants — through a driver AND a vehicle — and reads as one,
/// so a filter that asked only one parent, or none, returns the other customer's chip and fails the test.
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class TenancyThroughParentTests : IDisposable
{
    private static readonly Guid Acme = new("0f9d5a3c-7c1e-4d52-9a54-2b6f1f0c8e11");
    private static readonly Guid Globex = new("a1b2c3d4-0000-4000-8000-00000000beef");

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-through-parent-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_child_with_no_tenant_property_has_no_column_and_a_filter_through_each_parent()
    {
        await using var database = await StartDatabaseAsync();

        var chip = database.Context.Model.FindEntityType(typeof(Chip))!;

        Assert.Null(chip.FindProperty(Columns.TenantId));
        Assert.Contains(chip.GetDeclaredQueryFilters(), f => f.Key == "Tenant");
        Assert.All(chip.GetForeignKeys(), key => Assert.False(key.IsRequired));
        Assert.All(chip.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Cascade, key.DeleteBehavior));
        Assert.Equal(["Code"], Assert.Single(chip.GetIndexes(), i => i.IsUnique).Properties.Select(p => p.Name));
    }

    [Fact]
    public async Task A_tenant_in_flight_asks_each_parent_with_an_exists_on_its_key_and_its_tenant()
    {
        await using var database = await StartDatabaseAsync();

        string sql;
        using (Tenant.Use(Acme))
        {
            sql = database.Context.Set<Chip>().ToQueryString();
        }

        var where = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];
        Assert.Equal(2, where.Split("EXISTS").Length - 1);
        Assert.Contains("FROM \"Driver\"", where, StringComparison.Ordinal);
        Assert.Contains("FROM \"Vehicle\"", where, StringComparison.Ordinal);
        Assert.Contains("\"TenantId\" = @", where, StringComparison.Ordinal);
        Assert.Contains("\"DriverId\"", where, StringComparison.Ordinal);
        Assert.Contains("\"VehicleId\"", where, StringComparison.Ordinal);
        Assert.DoesNotContain(Acme.ToString(), where, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Across_asks_no_parent_at_all()
    {
        await using var database = await StartDatabaseAsync();

        string sql;
        using (Tenant.Across())
        {
            sql = database.Context.Set<Chip>().ToQueryString();
        }

        Assert.DoesNotContain("EXISTS", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Where_returns_the_tenants_chips_of_both_kinds_and_never_anothers()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        using (Tenant.Use(Acme))
        {
            Assert.Equal(["A-D1", "A-V1"], (await Chip.Select(c => c.Code)).Order(StringComparer.Ordinal));
            Assert.Equal(["A-D1"], await Chip.Where(c => c.DriverId != null).Select(c => c.Code));
        }

        using (Tenant.Use(Globex))
        {
            Assert.Equal(["G-D1", "G-V1"], (await Chip.Select(c => c.Code)).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task A_key_a_count_and_a_projection_through_the_parent_stay_inside_the_tenant()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        string globexChip;
        using (Tenant.Across())
        {
            globexChip = (await Chip.Where(c => c.Code == "G-D1").Select(c => c.Id)).Single();
        }

        using (Tenant.Use(Acme))
        {
            Assert.Empty(await Chip.Where(c => c.Id == globexChip).Select(c => c.Code));
            Assert.Equal(2, await Chip.Count(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(
                ["Ada"],
                await Chip.Where(c => c.Driver != null).Select(c => c.Driver!.Name));
        }
    }

    [Fact]
    public async Task An_include_from_the_root_brings_only_its_own_chips()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        using (Tenant.Use(Globex))
        {
            var drivers = await Driver.Include(d => d.Chips);

            var driver = Assert.Single(drivers);
            Assert.Equal(["G-D1"], driver.Chips.Select(c => c.Code));
        }
    }

    [Fact]
    public async Task Across_reads_every_tenants_chips_and_one_that_belongs_to_nobody()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();
        await database.Context.Database.ExecuteSqlAsync(
            $"INSERT INTO Chip (Id, Code, DriverId, VehicleId) VALUES ('loose', 'LOOSE', NULL, NULL)",
            TestContext.Current.CancellationToken);

        using (Tenant.Across())
        {
            Assert.Equal(5, await Chip.Count(cancellationToken: TestContext.Current.CancellationToken));
        }

        using (Tenant.Use(Acme))
        {
            Assert.DoesNotContain("LOOSE", await Chip.Select(c => c.Code));
        }
    }

    [Fact]
    public async Task The_read_face_has_no_tenant_column_a_navigation_to_each_parent_and_the_filter()
    {
        await using var database = await StartDatabaseAsync();
        await using var read = database.OpenRead();

        var face = read.Model.FindEntityType(typeof(ChipRead))!;

        Assert.Null(face.FindProperty(Columns.TenantId));
        Assert.Contains(face.GetDeclaredQueryFilters(), f => f.Key == "Tenant");
        Assert.NotNull(face.FindNavigation(nameof(ChipRead.Driver)));
        Assert.NotNull(face.FindNavigation(nameof(ChipRead.Vehicle)));
        Assert.All(face.GetForeignKeys(), key => Assert.False(key.IsRequired));
    }

    [Fact]
    public async Task A_chip_handed_out_under_the_tenants_own_driver_is_saved()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        using (Tenant.Use(Acme))
        {
            await Driver.Update("a-d", d => d.Hand("A-D2"), cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(["A-D1", "A-D2"], (await Chip.Where(c => c.DriverId == "a-d").Select(c => c.Code)).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task A_chip_added_to_a_driver_loaded_in_another_tenant_is_refused()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        Driver loaded;
        using (Tenant.Use(Acme))
        {
            loaded = await database.Context.Set<Driver>().Include(d => d.Chips)
                .SingleAsync(d => d.Id == "a-d", TestContext.Current.CancellationToken);
        }

        loaded.Hand("SMUGGLED");
        using (Tenant.Use(Globex))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(0, await StoredCountAsync(database, "SMUGGLED"));
    }

    [Fact]
    public async Task A_chip_written_alone_under_another_tenants_driver_is_refused_by_asking_the_database()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        using (Tenant.Use(Globex))
        {
            database.Context.Add(Chip.Under("a-d", "SMUGGLED"));

            var refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
            Assert.Contains("'Driver'", refused.Message, StringComparison.Ordinal);
        }

        Assert.Equal(0, await StoredCountAsync(database, "SMUGGLED"));
    }

    [Fact]
    public async Task A_chip_written_alone_even_under_the_tenants_own_driver_is_refused_since_children_go_through_their_aggregate()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        using (Tenant.Use(Acme))
        {
            database.Context.Add(Chip.Under("a-d", "A-D2"));

            var refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
            Assert.Contains("'Chip' was added on its own", refused.Message, StringComparison.Ordinal);
        }

        Assert.Equal(0, await StoredCountAsync(database, "A-D2"));
    }

    [Fact]
    public async Task A_chip_moved_to_another_tenants_driver_is_refused()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        using (Tenant.Use(Acme))
        {
            var chip = await database.Context.Set<Chip>().SingleAsync(c => c.Code == "A-D1", TestContext.Current.CancellationToken);
            chip.MoveTo("g-d");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_chip_loaded_across_tenants_is_not_changed_from_another_tenant()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        Chip chip;
        using (Tenant.Across())
        {
            chip = await database.Context.Set<Chip>().SingleAsync(c => c.Code == "A-D1", TestContext.Current.CancellationToken);
        }

        database.Context.Remove(chip);
        using (Tenant.Use(Globex))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(1, await StoredCountAsync(database, "A-D1"));
    }

    [Fact]
    public async Task A_chip_taken_back_from_its_driver_is_deleted_not_left_belonging_to_nobody()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        using (Tenant.Use(Acme))
        {
            await Driver.Update("a-d", d => d.TakeBack("A-D1"), cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, await StoredCountAsync(database, "A-D1"));
    }

    [Fact]
    public async Task Deleting_a_driver_takes_its_chips_and_leaves_the_vehicles()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync();

        using (Tenant.Use(Acme))
        {
            await Driver.Delete("a-d", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(["A-V1"], await Chip.Select(c => c.Code));
        }
    }

    private static async Task SeedAsync()
    {
        using (Tenant.Use(Acme))
        {
            await Driver.Named("a-d", "Ada", "A-D1").Save(cancellationToken: TestContext.Current.CancellationToken);
            await Vehicle.Plated("a-v", "AAA-111", "A-V1").Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        using (Tenant.Use(Globex))
        {
            await Driver.Named("g-d", "Gus", "G-D1").Save(cancellationToken: TestContext.Current.CancellationToken);
            await Vehicle.Plated("g-v", "GGG-999", "G-V1").Save(cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    // Straight to the table, past every filter: what the database holds, whoever is asking.
    private static async Task<int> StoredCountAsync(TestDatabase database, string code) =>
        (await database.Context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM Chip WHERE Code = {code}")
            .ToListAsync(TestContext.Current.CancellationToken)).Single();

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
