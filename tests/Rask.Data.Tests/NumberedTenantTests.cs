using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>
/// A table that already exists, mapped as it is: an identity key, a tenant numbered by an <c>int</c>, a name
/// unique within the tenant, and none of the framework's columns.
/// </summary>
public sealed class Destination : Aggregate<int>
{
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
        builder.HasIndex(d => new { d.Name, d.TenantId }).IsUnique();
    }
}

/// <summary>The same with a <c>long</c> tenant, and a child that keeps the tenant in its own column.</summary>
public sealed class Waybill : Aggregate<long>
{
    private readonly List<WaybillLine> _lines = [];

    private Waybill() { }

    public const Tenancy Scope = Tenancy.PerTenant;

    public long? TenantId { get; private set; }

    public string Reference { get; private set; } = "";

    public IReadOnlyCollection<WaybillLine> Lines => _lines;

    public static Waybill For(string reference, params string[] goods)
    {
        var waybill = new Waybill { Reference = reference };
        waybill._lines.AddRange(goods.Select(WaybillLine.Of));
        return waybill;
    }
}

public sealed class WaybillLine : Entity<long>
{
    private WaybillLine() { }

    public long? TenantId { get; private set; }

    public string Goods { get; private set; } = "";

    internal static WaybillLine Of(string goods) => new() { Goods = goods };
}

/// <summary>Not an entity, so nothing maps it: only what a wrongly typed tenant is told.</summary>
public sealed class MistypedTenant
{
    public string TenantId { get; set; } = "";
}

/// <summary>
/// A tenant-scoped table whose tenant is a number keeps each customer's rows apart exactly as a
/// <see cref="Guid" /> one does: every read is filtered, every insert is stamped, in the column's own type.
/// </summary>
/// <remarks>
/// Each test that proves isolation writes rows for TWO tenants and reads as one, so a filter that compared
/// nothing — or the wrong thing — returns the other customer's row and fails the test.
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class NumberedTenantTests : IDisposable
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-numbered-tenant-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task The_column_is_the_declared_property_in_its_own_type()
    {
        await using var database = await StartDatabaseAsync();

        var destination = database.Context.Model.FindEntityType(typeof(Destination))!;
        var column = destination.FindProperty(Columns.TenantId)!;

        Assert.Equal("Destinations", destination.GetTableName());
        Assert.False(column.IsShadowProperty());
        Assert.Equal(typeof(int?), column.ClrType);
        Assert.Equal("TenantId", column.GetColumnName());
    }

    [Fact]
    public async Task The_read_face_keeps_the_tenant_in_the_same_type()
    {
        await using var database = await StartDatabaseAsync();
        await using var read = database.OpenRead();

        var face = read.Model.GetEntityTypes().Single(t => t.GetTableName() == "Destinations");

        Assert.Equal(typeof(int?), face.FindProperty(Columns.TenantId)!.ClrType);
        Assert.Contains(face.GetDeclaredQueryFilters(), f => f.Key == "Tenant");
    }

    [Fact]
    public async Task The_index_that_names_the_tenant_stays_as_the_table_has_it()
    {
        await using var database = await StartDatabaseAsync();

        var index = Assert.Single(database.Context.Model.FindEntityType(typeof(Destination))!.GetIndexes());

        Assert.Equal(["Name", "TenantId"], index.Properties.Select(p => p.Name));
        Assert.True(index.IsUnique);
    }

    [Fact]
    public async Task Save_stamps_the_row_with_the_number_of_the_tenant_in_flight()
    {
        await using var database = await StartDatabaseAsync();

        var id = await SaveAsync(Acme, "Budapest");

        Assert.Equal(Acme, await StoredTenantAsync(database, id));
    }

    [Fact]
    public async Task The_aggregate_reads_its_own_tenant_after_the_save()
    {
        await using var database = await StartDatabaseAsync();
        var destination = Destination.Named("Budapest");

        using (Tenant.Use(Acme))
        {
            await destination.Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal(Acme, destination.TenantId);
    }

    [Fact]
    public async Task Where_returns_only_the_tenants_own_rows()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "Budapest");
        await SaveAsync(Globex, "Vienna");

        using (Tenant.Use(Acme))
        {
            Assert.Equal(["Budapest"], await Destination.Where(d => d.Name != "").Select(d => d.Name));
        }

        using (Tenant.Use(Globex))
        {
            Assert.Equal(["Vienna"], await Destination.OrderBy(d => d.Name).Take(15).Select(d => d.Name));
        }
    }

    [Fact]
    public async Task Count_counts_only_the_tenants_own_rows()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "Budapest");
        await SaveAsync(Acme, "Szeged");
        await SaveAsync(Globex, "Vienna");

        using (Tenant.Use(Globex))
        {
            Assert.Equal(1, await Destination.Count(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(0, await Destination.Count(d => d.Name == "Budapest", TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task The_filter_binds_the_tenant_as_a_parameter_and_never_writes_it_into_the_sql()
    {
        await using var database = await StartDatabaseAsync();
        await using var read = database.OpenRead();

        string write, face;
        using (Tenant.Use(987654))
        {
            write = database.Context.Set<Destination>().ToQueryString();
            face = read.Set<DestinationRead>().ToQueryString();
        }

        // A literal here would be cached with the query and served to the next tenant.
        Assert.Contains("\"TenantId\" = @", write.Split("\n\n")[^1], StringComparison.Ordinal);
        Assert.Contains("\"TenantId\" = @", face.Split("\n\n")[^1], StringComparison.Ordinal);
        Assert.DoesNotContain("987654", write.Split("\n\n")[^1], StringComparison.Ordinal);
        Assert.DoesNotContain("987654", face.Split("\n\n")[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_does_not_return_another_tenants_row_by_its_key()
    {
        await using var database = await StartDatabaseAsync();
        var theirs = await SaveAsync(Acme, "Budapest");

        using (Tenant.Use(Globex))
        {
            Assert.Null(await Destination.Find(theirs, cancellationToken: TestContext.Current.CancellationToken));
        }

        using (Tenant.Use(Acme))
        {
            Assert.NotNull(await Destination.Find(theirs, cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Update_cannot_reach_another_tenants_row()
    {
        await using var database = await StartDatabaseAsync();
        var theirs = await SaveAsync(Acme, "Budapest");

        using (Tenant.Use(Globex))
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => Destination.Update(
                theirs, d => d.Rename("taken"), cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Equal("Budapest", await StoredNameAsync(database, theirs));
    }

    [Fact]
    public async Task Update_changes_the_tenants_own_row_and_leaves_its_tenant_alone()
    {
        await using var database = await StartDatabaseAsync();
        var mine = await SaveAsync(Acme, "Budapest");

        using (Tenant.Use(Acme))
        {
            await Destination.Update(mine, d => d.Rename("Pest"), cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal("Pest", await StoredNameAsync(database, mine));
        Assert.Equal(Acme, await StoredTenantAsync(database, mine));
    }

    [Fact]
    public async Task Delete_cannot_reach_another_tenants_row()
    {
        await using var database = await StartDatabaseAsync();
        var theirs = await SaveAsync(Acme, "Budapest");

        using (Tenant.Use(Globex))
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => Destination.Delete(theirs, cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Equal("Budapest", await StoredNameAsync(database, theirs));
    }

    [Fact]
    public async Task Saving_a_copy_found_in_one_tenant_while_in_another_writes_nothing()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Acme, "Budapest");
        Destination found;
        using (Tenant.Use(Acme))
        {
            found = (await Destination.Find(id, cancellationToken: TestContext.Current.CancellationToken))!;
        }

        found.Rename("taken");
        using (Tenant.Use(Globex))
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => found.Save(cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Equal("Budapest", await StoredNameAsync(database, id));
        Assert.Equal(Acme, await StoredTenantAsync(database, id));
    }

    [Fact]
    public async Task A_row_that_belongs_to_no_tenant_is_invisible_to_every_tenant()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "Budapest");
        var orphan = await InsertOrphanAsync(database, "Nowhere");

        using (Tenant.Use(Acme))
        {
            Assert.Equal(["Budapest"], await Destination.Select(d => d.Name));
            Assert.Null(await Destination.Find(orphan, cancellationToken: TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => Destination.Update(
                orphan, d => d.Rename("claimed"), cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_row_cannot_be_moved_to_another_tenant_by_an_update()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Acme, "Budapest");

        using (Tenant.Use(Acme))
        {
            var tracked = await database.Context.Set<Destination>().SingleAsync(TestContext.Current.CancellationToken);
            database.Context.Entry(tracked).Property(d => d.TenantId).CurrentValue = Globex;

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        database.Context.ChangeTracker.Clear();
        Assert.Equal(Acme, await StoredTenantAsync(database, id));
    }

    [Fact]
    public async Task Across_sees_every_tenants_rows_and_the_ones_that_belong_to_none()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "Budapest");
        await SaveAsync(Globex, "Vienna");
        await InsertOrphanAsync(database, "Nowhere");

        using (Tenant.Across())
        {
            Assert.Equal(
                ["Budapest", "Nowhere", "Vienna"],
                (await Destination.Select(d => d.Name)).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task An_insert_inside_Across_is_refused_because_it_names_no_tenant()
    {
        await using var database = await StartDatabaseAsync();

        using (Tenant.Across())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Destination.Named("Budapest").Save(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(0, await Destination.Count(cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_read_with_no_tenant_throws_and_so_does_a_write()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "Budapest");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Destination.All);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Destination.Named("Vienna").Save(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_tenant_that_is_not_a_number_is_refused_rather_than_compared()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "Budapest");

        using (Tenant.Use(Guid.NewGuid()))
        {
            var read = await Assert.ThrowsAsync<InvalidOperationException>(async () => await Destination.All);
            var write = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Destination.Named("Vienna").Save(cancellationToken: TestContext.Current.CancellationToken));

            Assert.Contains("Tenant.Use(42)", read.Message, StringComparison.Ordinal);
            Assert.Contains("Tenant.Use(42)", write.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_number_too_large_for_the_column_is_refused_rather_than_truncated()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "Budapest");

        // 2^32 + 7 truncates to 7 — the other customer's number.
        using (Tenant.Use((1L << 32) + Acme))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await Destination.All);
        }
    }

    [Fact]
    public async Task Two_tenants_can_use_one_name_and_one_tenant_cannot_use_it_twice()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "Budapest");

        await SaveAsync(Globex, "Budapest");

        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(Acme, "Budapest"));
    }

    [Fact]
    public async Task A_long_tenant_filters_and_stamps_the_root_and_its_child()
    {
        await using var database = await StartDatabaseAsync();
        const long Big = 5_000_000_000;
        using (Tenant.Use(Big))
        {
            await Waybill.For("W-BIG", "steel").Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        using (Tenant.Use(Acme))
        {
            await Waybill.For("W-ACME", "glass", "sand").Save(cancellationToken: TestContext.Current.CancellationToken);
        }

        using (Tenant.Use(Big))
        {
            Assert.Equal(["W-BIG"], await Waybill.Select(w => w.Reference));
            Assert.Equal(["steel"], await WaybillLine.Select(l => l.Goods));
        }

        using (Tenant.Across())
        {
            var lines = await database.Context.Set<WaybillLine>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal([Acme, Acme, Big], lines.Select(l => l.TenantId!.Value).Order());
        }
    }

    [Fact]
    public void A_number_travels_as_a_guid_whose_last_eight_bytes_are_the_number()
    {
        using (Tenant.Use(42))
        {
            Assert.Equal(new Guid("00000000-0000-0000-0000-00000000002a"), Current.Tenant);
        }

        using (Tenant.Use(long.MaxValue))
        {
            Assert.Equal(new Guid("00000000-0000-0000-7fff-ffffffffffff"), Current.Tenant);
        }
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(-1L)]
    [InlineData(int.MaxValue)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void A_number_reads_back_as_itself(long number)
    {
        var carried = TenantNumber.ToGuid(number);

        var isNumber = TenantNumber.TryRead(carried, out var read);

        Assert.True(isNumber);
        Assert.Equal(number, read);
    }

    [Fact]
    public void An_identifier_a_library_generated_is_never_read_as_a_number()
    {
        Guid[] identifiers = [Guid.NewGuid(), Guid.CreateVersion7(), new("00000000-0000-0001-0000-00000000002a")];

        var read = identifiers.Select(id => TenantNumber.TryRead(id, out _));

        Assert.All(read, Assert.False);
    }

    [Fact]
    public void A_tenant_declared_in_a_type_no_tenant_is_kept_in_is_refused_with_the_types_that_are()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => TenantColumn.TypeFor(typeof(MistypedTenant)));

        Assert.Contains("'String'", refused.Message, StringComparison.Ordinal);
        Assert.Contains("public int? TenantId { get; private set; }", refused.Message, StringComparison.Ordinal);
    }

    private static async Task<int> SaveAsync(int tenant, string name)
    {
        using (Tenant.Use(tenant))
        {
            var destination = Destination.Named(name);
            await destination.Save(cancellationToken: TestContext.Current.CancellationToken);
            return destination.Id;
        }
    }

    // Straight to the table, past every filter and stamp: what the database holds, whoever is asking.
    private static async Task<int?> StoredTenantAsync(TestDatabase database, int id) =>
        (await database.Context.Database
            .SqlQuery<int?>($"SELECT TenantId AS Value FROM Destinations WHERE Id = {id}")
            .ToListAsync(TestContext.Current.CancellationToken)).Single();

    private static async Task<string> StoredNameAsync(TestDatabase database, int id) =>
        (await database.Context.Database
            .SqlQuery<string>($"SELECT Name AS Value FROM Destinations WHERE Id = {id}")
            .ToListAsync(TestContext.Current.CancellationToken)).Single();

    private static async Task<int> InsertOrphanAsync(TestDatabase database, string name)
    {
        await database.Context.Database.ExecuteSqlAsync(
            $"INSERT INTO Destinations (Name, TenantId) VALUES ({name}, NULL)", TestContext.Current.CancellationToken);

        return (await database.Context.Database
            .SqlQuery<int>($"SELECT Id AS Value FROM Destinations WHERE Name = {name}")
            .ToListAsync(TestContext.Current.CancellationToken)).Single();
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
