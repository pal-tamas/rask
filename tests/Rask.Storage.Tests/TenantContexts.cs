using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Storage.Tests;

/// <summary>A tenant-scoped table whose tenants are numbered: <c>IPerTenant&lt;int&gt;</c>, with no declared column.</summary>
public sealed class NumberedLedger : Aggregate<Guid>, IPerTenant<int>
{
    public string Name { get; private set; } = "";
}

/// <summary>The same numbered by <c>long</c>, declaring its column.</summary>
public sealed class LongLedger : Aggregate<Guid>, IPerTenant<long>
{
    public long? TenantId { get; private set; }

    public string Name { get; private set; } = "";
}

/// <summary>A tenant-scoped table whose tenants are Guids, as plain <c>IPerTenant</c> gives it.</summary>
public sealed class GuidLedger : Aggregate<Guid>, IPerTenant
{
    public string Name { get; private set; } = "";
}

/// <summary>What the generator registers for the ledgers, said by hand: this project does not run it.</summary>
internal static class LedgerConventions
{
    private static readonly Lazy<bool> Declared = new(static () =>
    {
        ConventionRegistry.Declare(typeof(NumberedLedger), TraitSet.PerTenant);
        ConventionRegistry.DeclareTenantKey(typeof(NumberedLedger), typeof(int));
        ConventionRegistry.Declare(typeof(LongLedger), TraitSet.PerTenant);
        ConventionRegistry.DeclareTenantKey(typeof(LongLedger), typeof(long));
        ConventionRegistry.Declare(typeof(GuidLedger), TraitSet.PerTenant);
        return true;
    });

    internal static void Ensure() => _ = Declared.Value;
}

/// <summary>An app whose tenants are numbered: storage mapped first, Rask's conventions after.</summary>
public sealed class NumberedTenantContext(DbContextOptions<NumberedTenantContext> options) : DbContext(options), ITenantScoped
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        LedgerConventions.Ensure();
        modelBuilder.Entity<NumberedLedger>();
        modelBuilder.AddRaskStorage();
        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>The same app with the two calls the other way round, as <c>RaskAppDbContext</c> makes them.</summary>
public sealed class ConventionsFirstNumberedTenantContext(DbContextOptions<ConventionsFirstNumberedTenantContext> options)
    : DbContext(options), ITenantScoped
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        LedgerConventions.Ensure();
        modelBuilder.Entity<NumberedLedger>();
        modelBuilder.ApplyRaskConventions(this);
        modelBuilder.AddRaskStorage();
    }
}

/// <summary>An app whose tenants are numbered by <c>long</c>.</summary>
public sealed class LongTenantContext(DbContextOptions<LongTenantContext> options) : DbContext(options), ITenantScoped
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        LedgerConventions.Ensure();
        modelBuilder.Entity<LongLedger>();
        modelBuilder.AddRaskStorage();
        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>An app whose tenant-scoped tables keep Guids.</summary>
public sealed class GuidTenantContext(DbContextOptions<GuidTenantContext> options) : DbContext(options), ITenantScoped
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        LedgerConventions.Ensure();
        modelBuilder.Entity<GuidLedger>();
        modelBuilder.AddRaskStorage();
        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>Rask's conventions and nothing tenant-scoped: what a Guid app's stored-file table is compared with.</summary>
public sealed class ConventionsContext(DbContextOptions<ConventionsContext> options) : DbContext(options), ITenantScoped
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddRaskStorage();
        modelBuilder.ApplyRaskConventions(this);
    }
}
