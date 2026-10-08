using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Rask.Data.Tests;

/// <summary>The shape a table had while <c>TenantId</c> was a property of the base class.</summary>
public abstract class FormerAggregate
{
    public Guid Id { get; protected set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public Guid? TenantId { get; private set; }

    public int Version { get; private set; }
}

public sealed class FormerPallet : FormerAggregate
{
    public string Reference { get; private set; } = "";
}

public sealed class FormerCrate : FormerAggregate
{
    public string Reference { get; private set; } = "";
}

/// <summary>Tenant-scoped, and silent about the column: it is a shadow one.</summary>
public sealed class Pallet : Aggregate<Guid>
{
    private Pallet() { }

    public const Tenancy Scope = Tenancy.PerTenant;

    public string Reference { get; private set; } = "";
}

/// <summary>Tenant-scoped, and it declares the column because it reads it.</summary>
public sealed class Crate : Aggregate<Guid>
{
    private Crate() { }

    public const Tenancy Scope = Tenancy.PerTenant;

    public Guid? TenantId { get; private set; }

    public string Reference { get; private set; } = "";
}

/// <summary>
/// <c>TenantId</c> left the base class: an entity declares it or leaves it a shadow column. A database created
/// before that owes no migration for it — where a freshly created table lists the column differs, and nothing else.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class TenantColumnMigrationTests
{
    private sealed class FormerContext(DbContextOptions<FormerContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            Map<FormerPallet>(modelBuilder, "Pallet");
            Map<FormerCrate>(modelBuilder, "Crate");
        }

        private static void Map<T>(ModelBuilder modelBuilder, string table)
            where T : FormerAggregate =>
            modelBuilder.Entity<T>(b =>
            {
                b.ToTable(table);
                b.Property(x => x.Id).ValueGeneratedNever();
                b.Property(x => x.Version).IsConcurrencyToken();
                b.HasIndex(nameof(FormerAggregate.TenantId), "Reference").IsUnique();
            });
    }

    private sealed class TodayContext(DbContextOptions<TodayContext> options) : DbContext(options), ITenantScoped
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Pallet>().HasIndex(p => p.Reference).IsUnique();
            modelBuilder.Entity<Crate>().HasIndex(c => c.Reference).IsUnique();
            modelBuilder.ApplyRaskConventions(this);
        }
    }

    [Fact]
    public void A_database_created_while_the_tenant_was_on_the_base_class_owes_no_migration()
    {
        using var former = new FormerContext(new DbContextOptionsBuilder<FormerContext>().UseSqlite("Data Source=:memory:").Options);
        using var today = new TodayContext(new DbContextOptionsBuilder<TodayContext>().UseSqlite("Data Source=:memory:").Options);
        var differ = today.GetService<IMigrationsModelDiffer>();

        var forward = differ.GetDifferences(RelationalModelOf(former), RelationalModelOf(today));
        var back = differ.GetDifferences(RelationalModelOf(today), RelationalModelOf(former));

        Assert.Empty(forward);
        Assert.Empty(back);

        // The differ is looking: against nothing at all it asks for both tables.
        Assert.NotEmpty(differ.GetDifferences(null, RelationalModelOf(today)));
    }

    [Theory]
    [InlineData(typeof(Pallet))]
    [InlineData(typeof(Crate))]
    public void Every_index_leads_with_the_tenant_whether_the_column_is_declared_or_not(Type entity)
    {
        using var today = new TodayContext(new DbContextOptionsBuilder<TodayContext>().UseSqlite("Data Source=:memory:").Options);

        var index = Assert.Single(today.Model.FindEntityType(entity)!.GetIndexes());

        Assert.Equal([Columns.TenantId, "Reference"], index.Properties.Select(p => p.Name));
        Assert.True(index.IsUnique);
    }

    private static IRelationalModel RelationalModelOf(DbContext context) =>
        context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
}
