using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Providers.E2E.Tests;

/// <summary>A tenant whose key is the text of a Guid, as TA's <c>Partner</c> is.</summary>
public sealed class Agency : Aggregate<string>, IDeletable
{
    public const string InUse = "The agency still has data.";

    private Agency() { }

    public static Agency Opened(Guid id) => new() { Id = id.ToString() };

    public static void Configure(EntityTypeBuilder<Agency> builder)
    {
        builder.Property(agency => agency.Id).HasMaxLength(64);
        builder.WhenReferenced(InUse);
    }
}

/// <summary>One of an agency's rows: its tenant a Guid, kept as text in the column.</summary>
public sealed class Voyage : Aggregate<Guid>, IDeletable, IBelongsTo<Agency>
{
    private Voyage() { }

    public Guid? TenantId { get; private set; }

    public static Voyage Of(Guid agency) => new() { Id = Guid.NewGuid(), TenantId = agency };

    public static void Configure(EntityTypeBuilder<Voyage> builder) =>
        builder.Property(voyage => voyage.TenantId).HasConversion<string>().HasMaxLength(64);
}

public sealed class AgencyDbContext(DbContextOptions<AgencyDbContext> options) : DbContext(options), ITenantScoped
{
    public const string Schema = "rask_tenant_refs";

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder)
            .ConstraintNames(n => n.Kind == ConstraintKind.ForeignKey ? $"{n.Table}_{n.Columns[0]}_fkey" : null);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (string.Equals(Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            modelBuilder.HasDefaultSchema(Schema);
        }

        modelBuilder.Entity<Agency>(Agency.Configure);
        modelBuilder.Entity<Voyage>(Voyage.Configure);
        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>
///     A table that belongs to the app's tenant refers to it on each provider: a tenant that still has rows is not deleted —
///     on SQLite too — and the refusal says the tenant's sentence, though its key and the rows' are of two types.
/// </summary>
public abstract class TenantReferenceParityTests(ProviderLeg leg) : ParityTests<AgencyDbContext>(leg)
{
    [Fact]
    public async Task A_tenant_that_still_has_rows_is_not_deleted_and_says_why()
    {
        RequireServer();
        await using var db = Open();
        var agency = Guid.NewGuid();
        db.Add(Agency.Opened(agency));
        await db.SaveChangesAsync(Ct);
        using (Tenant.Use(agency))
        {
            db.Add(Voyage.Of(agency));
            await db.SaveChangesAsync(Ct);
        }

        db.Remove(await db.Set<Agency>().SingleAsync(Ct));
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => db.SaveChangesAsync(Ct));

        Assert.Equal(Agency.InUse, Assert.Single(refused.Failures).Message);
    }

    [Fact]
    public async Task A_tenant_without_rows_is_deleted()
    {
        RequireServer();
        await using var db = Open();
        db.Add(Agency.Opened(Guid.NewGuid()));
        await db.SaveChangesAsync(Ct);

        db.Remove(await db.Set<Agency>().SingleAsync(Ct));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(0, await db.Set<Agency>().CountAsync(Ct));
    }

    [Fact]
    public async Task A_row_of_a_tenant_that_is_not_there_is_refused()
    {
        RequireServer();
        await using var db = Open();
        var missing = Guid.NewGuid();
        using var inIt = Tenant.Use(missing);

        db.Add(Voyage.Of(missing));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    // The agency is the app's tenant, as the generated registration of the aggregate that says ITenant names it.
    private protected override AgencyDbContext Open()
    {
        CurrentRows.Use(new CurrentRow<AgencyRead, string>(
            CurrentRowKind.Tenant, typeof(Agency), static key => row => row.Id == key, static row => row.Id));
        return Leg.Open<AgencyDbContext>(o => new AgencyDbContext(o));
    }
}

public sealed class SqliteTenantReferenceParityTests() : TenantReferenceParityTests(ProviderLeg.Sqlite("tenant-references"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerTenantReferenceParityTests() : TenantReferenceParityTests(ProviderLeg.SqlServer("rask_e2e_tenant_references"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresTenantReferenceParityTests() : TenantReferenceParityTests(ProviderLeg.Postgres(AgencyDbContext.Schema));
