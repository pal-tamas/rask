using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>
/// A table that already exists, whose tenant is a <see cref="Guid" /> kept as TEXT: a string key and a text
/// <c>TenantId</c> column, as a database another tool made often has it.
/// </summary>
public sealed class Branch : Aggregate<string>, IHasForm, IDeletable, IBelongsTo<GuidShop>
{
    private Branch() { }

    public Guid? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public static Branch Named(string key, string name) => new() { Id = key, Name = name };

    public void Rename(string name) => Name = name;

    public static void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");
        builder.Property(b => b.Name).IsRequired().HasMaxLength(255);
        builder.Property(b => b.TenantId).HasConversion<string>().HasMaxLength(64);
        builder.HasIndex(b => new { b.Name, b.TenantId }).IsUnique();
    }
}

/// <summary>A market stand of a tenant's, its code unique among every tenant's stands and the ones of none.</summary>
public sealed class Stand : Aggregate<string>, IHasForm, IDeletable, IBelongsTo<GuidShop>
{
    public const string CodeTaken = "A stand with this code already exists.";

    private Stand() { }

    public Guid? TenantId { get; private set; }

    public string Code { get; private set; } = "";

    public static Stand Coded(string key, string code) => new() { Id = key, Code = code };

    public static void Configure(EntityTypeBuilder<Stand> builder)
    {
        builder.ToTable("Stands");
        builder.Property(s => s.TenantId).HasConversion<string>().HasMaxLength(64);
        builder.HasIndex(s => s.Code).IsUnique(CodeTaken);
    }
}

/// <summary>
/// A tenant that is a <see cref="Guid" /> declared with a text conversion keeps each customer's rows apart exactly
/// as a native one does: every read is filtered, every insert is stamped, and the column holds the text.
/// </summary>
/// <remarks>
/// Each test that proves isolation writes rows for TWO tenants and reads as one, so a filter that compared
/// nothing — or the wrong thing — returns the other customer's row and fails the test.
/// </remarks>
public sealed class TextTenantTests : IDisposable
{
    private static readonly Guid Acme = new("0f9d5a3c-7c1e-4d52-9a54-2b6f1f0c8e11");
    private static readonly Guid Globex = new("a1b2c3d4-0000-4000-8000-00000000beef");

    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task The_column_is_the_declared_guid_stored_as_text()
    {
        await using var database = await StartDatabaseAsync();

        var column = database.Context.Model.FindEntityType(typeof(Branch))!.FindProperty(Columns.TenantId)!;

        Assert.False(column.IsShadowProperty());
        Assert.Equal(typeof(Guid?), column.ClrType);
        Assert.Equal(typeof(string), column.GetTypeMapping().Converter?.ProviderClrType);
    }

    [Fact]
    public async Task The_read_face_filters_by_the_same_text()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "b-1", "Budapest");
        await SaveAsync(Globex, "b-2", "Vienna");
        await using var read = database.OpenRead();

        var face = read.Model.GetEntityTypes().Single(t => t.GetTableName() == "Branches");
        List<string> names;
        using (Tenant.Use(Acme))
        {
            names = await read.Set<BranchRead>().Select(b => b.Name).ToListAsync(TestContext.Current.CancellationToken);
        }

        Assert.Contains(face.GetDeclaredQueryFilters(), f => f.Key == "Tenant");
        Assert.Equal(["Budapest"], names);
    }

    [Fact]
    public async Task Save_stamps_the_row_with_the_text_of_the_tenant_in_flight()
    {
        await using var database = await StartDatabaseAsync();

        await SaveAsync(Acme, "b-1", "Budapest");

        Assert.Equal(Acme.ToString(), await StoredTenantAsync(database, "b-1"));
    }

    [Fact]
    public async Task Where_returns_only_the_tenants_own_rows()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "b-1", "Budapest");
        await SaveAsync(Globex, "b-2", "Vienna");

        using (Tenant.Use(Acme))
        {
            Assert.Equal(["Budapest"], await Branch.Where(b => b.Name != "").Select(b => b.Name));
        }

        using (Tenant.Use(Globex))
        {
            Assert.Equal(["Vienna"], await Branch.OrderBy(b => b.Name).Take(15).Select(b => b.Name));
        }
    }

    [Fact]
    public async Task The_filter_binds_the_tenant_as_a_parameter_in_the_columns_own_type()
    {
        await using var database = await StartDatabaseAsync();

        string sql;
        using (Tenant.Use(Acme))
        {
            sql = database.Context.Set<Branch>().ToQueryString();
        }

        // A literal here would be cached with the query and served to the next tenant.
        var statement = sql.Split("\n\n")[^1];
        Assert.Contains("\"tenant_id\" = @", statement, StringComparison.Ordinal);
        Assert.DoesNotContain(Acme.ToString(), statement, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_update_and_delete_cannot_reach_another_tenants_row_by_its_key()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "b-1", "Budapest");

        using (Tenant.Use(Globex))
        {
            Assert.Null(await Branch.Find("b-1", cancellationToken: TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => Branch.Update(
                "b-1", b => b.Rename("taken"), cancellationToken: TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => Branch.Delete("b-1", cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Equal("Budapest", await StoredNameAsync(database, "b-1"));
        Assert.Equal(Acme.ToString(), await StoredTenantAsync(database, "b-1"));
    }

    [Fact]
    public async Task Update_changes_the_tenants_own_row_and_leaves_its_tenant_alone()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "b-1", "Budapest");

        using (Tenant.Use(Acme))
        {
            await Branch.Update("b-1", b => b.Rename("Pest"), cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal("Pest", await StoredNameAsync(database, "b-1"));
        Assert.Equal(Acme.ToString(), await StoredTenantAsync(database, "b-1"));
    }

    [Fact]
    public async Task A_read_with_no_tenant_throws_and_so_does_a_write()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "b-1", "Budapest");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Branch.All);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Branch.Named("b-2", "Vienna").Save(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_tenants_can_use_one_name_and_one_tenant_cannot_use_it_twice()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "b-1", "Budapest");

        await SaveAsync(Globex, "b-2", "Budapest");

        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(Acme, "b-3", "Budapest"));
    }

    [Fact]
    public async Task A_tenant_read_from_text_a_tool_wrote_is_found_in_the_tenant_it_names()
    {
        await using var database = await StartDatabaseAsync();
        await database.Context.Database.ExecuteSqlAsync(
            $"INSERT INTO Branches (id, name, tenant_id) VALUES ('b-9', 'Szeged', {Acme.ToString()})", TestContext.Current.CancellationToken);

        using (Tenant.Use(Acme))
        {
            Assert.Equal(["Szeged"], await Branch.Select(b => b.Name));
        }

        using (Tenant.Use(Globex))
        {
            Assert.Empty(await Branch.Select(b => b.Name));
        }
    }

    [Fact]
    public async Task A_row_saved_with_no_tenant_is_no_tenant_s_to_read_update_or_delete()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "b-1", "Budapest");
        await database.Context.Database.ExecuteSqlAsync(
            $"INSERT INTO Branches (id, name, tenant_id) VALUES ('b-0', 'Nowhere', NULL)", TestContext.Current.CancellationToken);

        List<string> names;
        Exception? updated;
        Exception? deleted;
        using (Tenant.Use(Acme))
        {
            names = await Branch.Where(b => b.Name != "").Select(b => b.Name);
            updated = await Record.ExceptionAsync(() => Branch.Update(
                "b-0", b => b.Rename("taken"), cancellationToken: TestContext.Current.CancellationToken));
            deleted = await Record.ExceptionAsync(
                () => Branch.Delete("b-0", cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Equal(["Budapest"], names);
        Assert.IsType<KeyNotFoundException>(updated);
        Assert.IsType<KeyNotFoundException>(deleted);
        Assert.Null(await StoredTenantAsync(database, "b-0"));
        Assert.Equal("Nowhere", await StoredNameAsync(database, "b-0"));
    }

    [Fact]
    public async Task A_unique_index_that_names_no_tenant_refuses_a_tenant_s_row_that_repeats_a_row_of_none()
    {
        await using var database = await StartDatabaseAsync();
        await database.Context.Database.ExecuteSqlAsync(
            $"INSERT INTO Stands (id, code, tenant_id) VALUES ('s-0', 'K-1', NULL)", TestContext.Current.CancellationToken);

        Exception? refused;
        using (Tenant.Use(Acme))
        {
            refused = await Record.ExceptionAsync(
                () => Stand.Coded("s-1", "K-1").Save(cancellationToken: TestContext.Current.CancellationToken));
        }

        var failure = Assert.Single(Assert.IsType<RaskValidationException>(refused).Failures);
        Assert.Equal(["Code"], failure.Fields);
        Assert.Equal(Stand.CodeTaken, failure.Message);
    }

    [Fact]
    public async Task The_edit_model_of_another_tenant_s_row_is_null_as_its_find_is()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, "b-1", "Budapest");

        BranchModel? own;
        BranchModel? other;
        Branch? found;
        using (Tenant.Use(Acme))
        {
            own = await Branch.Model("b-1", cancellationToken: TestContext.Current.CancellationToken);
        }

        using (Tenant.Use(Globex))
        {
            other = await Branch.Model("b-1", cancellationToken: TestContext.Current.CancellationToken);
            found = await Branch.Find("b-1", cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal("Budapest", own?.Name);
        Assert.Null(other);
        Assert.Null(found);
    }

    private static async Task SaveAsync(Guid tenant, string key, string name)
    {
        using (Tenant.Use(tenant))
        {
            await Branch.Named(key, name).Save(cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    // Straight to the table, past every filter and stamp: what the database holds, whoever is asking.
    private static async Task<string?> StoredTenantAsync(TestDatabase database, string key) =>
        (await database.Context.Database
            .SqlQuery<string?>($"SELECT tenant_id AS Value FROM Branches WHERE id = {key}")
            .ToListAsync(TestContext.Current.CancellationToken)).Single();

    private static async Task<string> StoredNameAsync(TestDatabase database, string key) =>
        (await database.Context.Database
            .SqlQuery<string>($"SELECT name AS Value FROM Branches WHERE id = {key}")
            .ToListAsync(TestContext.Current.CancellationToken)).Single();

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));
}
