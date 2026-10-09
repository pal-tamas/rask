using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>A one-value value object the usual way: a positional record.</summary>
public sealed record ContactName(string Value);

/// <summary>The shape RASK084 asks for: nothing public to set, built through a private constructor.</summary>
public sealed class ContactCode
{
    private ContactCode() { }

    public string Value { get; private set; } = "";

    public static ContactCode Of(string value) => new() { Value = value };
}

/// <summary>A one-value value object that is a struct.</summary>
public readonly record struct ContactRank(int Value);

/// <summary>An adopted table whose name is a value object — and is what the unique rule is over.</summary>
public sealed class Contact : Aggregate<int>
{
    public const string NameTaken = "Ilyen néven már van névjegy.";
    public const string CodeTaken = "This code is already in use.";

    private Contact() { }

    public const Timestamps Stamps = Timestamps.None;

    public const Concurrency Checks = Concurrency.None;

    public const Tenancy Scope = Tenancy.PerTenant;

    public int? TenantId { get; private set; }

    public ContactName Name { get; private set; } = new("");

    public ContactCode? Code { get; private set; }

    public ContactRank Rank { get; private set; }

    public static Contact Named(string name, string? code = null, int rank = 0) =>
        new() { Name = new ContactName(name), Code = code is null ? null : ContactCode.Of(code), Rank = new ContactRank(rank) };

    public void Rename(string name) => Name = new ContactName(name);

    public static void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("Contacts");
        builder.Property(c => c.Name).IsRequired().HasMaxLength(255);
        builder.HasIndex(c => new { c.Name, c.TenantId }).IsUnique(NameTaken);
        builder.HasIndex(c => c.Code).IsUnique(CodeTaken);
    }
}

/// <summary>The same table as a one-value value object used to be mapped, for the columns to be compared with.</summary>
public sealed class ComplexContact
{
    public int Id { get; set; }

    public int? TenantId { get; set; }

    public ContactName Name { get; set; } = new("");

    public ContactCode? Code { get; set; }

    public ContactRank Rank { get; set; }
}

public sealed class ComplexContactContext(DbContextOptions<ComplexContactContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ComplexContact>(e =>
        {
            e.ToTable("Contacts");
            e.ComplexProperty(x => x.Name, b => b.Property(v => v.Value).HasColumnName("Name").HasMaxLength(255));
            e.ComplexProperty(x => x.Code, b => b.Property(v => v.Value).HasColumnName("Code"));
            e.ComplexProperty(x => x.Rank, b => b.Property(v => v.Value).HasColumnName("Rank"));
        });
}

/// <summary>
/// A value object that holds one value is stored as that value's column — a converted scalar — so it can be
/// indexed and made unique like any other column, and the table is the one the old complex mapping produced.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ValueObjectColumnTests : IDisposable
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-vo-column-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task The_value_object_is_one_scalar_column_named_after_the_property_with_its_own_facets()
    {
        await using var database = await StartDatabaseAsync();

        var name = database.Context.Model.FindEntityType(typeof(Contact))!.FindProperty(nameof(Contact.Name))!;

        Assert.Equal("Name", name.GetColumnName());
        Assert.Equal(typeof(string), name.GetValueConverter()!.ProviderClrType);
        Assert.Equal(255, name.GetMaxLength());
        Assert.False(name.IsNullable);
    }

    [Fact]
    public async Task The_table_has_the_columns_the_complex_mapping_gave_it()
    {
        await using var database = await StartDatabaseAsync();
        using var complex = new ComplexContactContext(new DbContextOptionsBuilder<ComplexContactContext>().UseSqlite("Data Source=:memory:").Options);

        var now = ColumnsOf(database.Context);
        var before = ColumnsOf(complex);

        Assert.Equal(before, now);
    }

    [Fact]
    public async Task Every_shape_of_one_value_type_is_saved_and_read_back_whole()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Acme, Contact.Named("Ada", code: "A-1", rank: 3));
        var bare = await SaveAsync(Acme, Contact.Named("Bob"));

        Contact ada, bob;
        using (Tenant.Use(Acme))
        {
            ada = (await Contact.Find(id, cancellationToken: TestContext.Current.CancellationToken))!;
            bob = (await Contact.Find(bare, cancellationToken: TestContext.Current.CancellationToken))!;
        }

        Assert.Equal(new ContactName("Ada"), ada.Name);
        Assert.Equal("A-1", ada.Code!.Value);
        Assert.Equal(new ContactRank(3), ada.Rank);
        Assert.Null(bob.Code);
    }

    [Fact]
    public async Task An_index_over_the_value_objects_column_builds_and_keeps_two_tenants_apart()
    {
        await using var database = await StartDatabaseAsync();
        var index = database.Context.Model.FindEntityType(typeof(Contact))!.GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(["Name", "TenantId"]));
        await SaveAsync(Acme, Contact.Named("Ada"));

        await SaveAsync(Globex, Contact.Named("Ada"));

        Assert.True(index.IsUnique);
        using (Tenant.Across())
        {
            Assert.Equal(2, await Contact.Count(cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_duplicate_is_refused_with_the_message_under_the_property_not_under_its_inner_value()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Contact.Named("Ada"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => SaveAsync(Acme, Contact.Named("Ada")));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Contact.NameTaken, failure.Message);
        Assert.Equal(["Name"], failure.Fields);
        Assert.IsType<DbUpdateException>(refused.InnerException);
    }

    [Fact]
    public async Task A_nullable_value_object_collides_only_when_it_has_a_value()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Contact.Named("Ada"));
        await SaveAsync(Acme, Contact.Named("Bob"));
        await SaveAsync(Acme, Contact.Named("Cy", code: "C-1"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => SaveAsync(Acme, Contact.Named("Di", code: "C-1")));

        Assert.Equal(["Code"], Assert.Single(refused.Failures).Fields);
    }

    [Fact]
    public async Task The_declared_rule_is_asked_as_a_query_over_the_value_objects_column_when_the_index_is_gone()
    {
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<DomainContext>();
        services.AddDeclaredRuleChecks();
        services.AddDbContextFactory<DomainContext>((sp, o) => o
            .UseSqlite($"Data Source={_dbPath};Pooling=False")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite($"Data Source={_dbPath};Pooling=False"));
        await using var app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(app);
        try
        {
            await using var context = await app.GetRequiredService<IDbContextFactory<DomainContext>>()
                .CreateDbContextAsync(TestContext.Current.CancellationToken);
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            await context.Database.ExecuteSqlAsync(
                $"DROP INDEX \"IX_Contacts_Name_TenantId\"", TestContext.Current.CancellationToken);
            await SaveAsync(Acme, Contact.Named("Ada"));

            var refused = await Assert.ThrowsAsync<RaskValidationException>(() => SaveAsync(Acme, Contact.Named("Ada")));
            await SaveAsync(Globex, Contact.Named("Ada"));

            Assert.Equal(["Name"], Assert.Single(refused.Failures).Fields);
            Assert.Null(refused.InnerException);
        }
        finally
        {
            Db.Reset();
        }
    }

    [Fact]
    public async Task The_read_face_filters_and_sorts_by_the_value_as_the_primitive_it_is()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Contact.Named("Adam", rank: 2));
        await SaveAsync(Acme, Contact.Named("Ada", rank: 9));
        await SaveAsync(Acme, Contact.Named("Bob", rank: 5));

        List<string> names;
        using (Tenant.Use(Acme))
        {
            names = await Contact.Where(c => c.Name.StartsWith("Ada") && c.Rank > 1).OrderBy(c => c.Name).Select(c => c.Name);
        }

        Assert.Equal(["Ada", "Adam"], names);
    }

    [Fact]
    public async Task On_the_write_model_the_whole_value_compares_in_sql_and_reaching_inside_it_does_not_translate()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Contact.Named("Ada"));
        var wanted = new ContactName("Ada");

        using (Tenant.Use(Acme))
        {
            var whole = await database.Context.Set<Contact>().AsNoTracking()
                .Where(c => c.Name == wanted).ToListAsync(TestContext.Current.CancellationToken);
            var inside = await Assert.ThrowsAsync<InvalidOperationException>(() => database.Context.Set<Contact>()
                .Where(c => c.Name.Value.Contains("d")).ToListAsync(TestContext.Current.CancellationToken));

            Assert.Single(whole);
            Assert.Contains("could not be translated", inside.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_edit_form_carries_the_value_and_a_rename_writes_it_back()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Acme, Contact.Named("Ada"));

        using (Tenant.Use(Acme))
        {
            var model = (await Contact.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
            model.Name = "Adele";
            await Contact.Update(id, model, cancellationToken: TestContext.Current.CancellationToken);
            await Contact.Update(id, c => c.Rename("Adeline"), cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(["Adeline"], await Contact.Select(c => c.Name));
        }
    }

    private static List<string> ColumnsOf(DbContext context) =>
        [.. context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables
            .Single(t => t.Name == "Contacts").Columns
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => $"{c.Name} {c.StoreType} {(c.IsNullable ? "NULL" : "NOT NULL")}")];

    private static async Task<int> SaveAsync(int tenant, Contact contact)
    {
        using (Tenant.Use(tenant))
        {
            await contact.Save(cancellationToken: TestContext.Current.CancellationToken);
            return contact.Id;
        }
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
