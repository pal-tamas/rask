using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>A tenant-scoped aggregate: one const, nothing else configured.</summary>
public sealed class Ledger : Aggregate<Guid>
{
    private Ledger() { }

    public const Tenancy Scope = Tenancy.PerTenant;

    public string Reference { get; private set; } = "";

    public static Ledger For(string reference) => new() { Id = Guid.NewGuid(), Reference = reference };
}

/// <summary>A table whose tenants are numbered: the declared property's type is the column's.</summary>
public sealed class Depot : Aggregate<int>
{
    private Depot() { }

    public const Tenancy Scope = Tenancy.PerTenant;

    public int? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public static Depot Named(string name) => new() { Name = name };
}

/// <summary>
/// The shape the accounts table has: a NULLABLE tenant inside a unique index. This is the one the providers
/// disagree about, so it is modelled here rather than only reasoned about.
/// </summary>
public sealed class Member : Aggregate<Guid>
{
    private Member() { }

    public string Email { get; private set; } = "";

    public Guid? Owner { get; private set; }

    public static Member For(string email, Guid? owner) =>
        new() { Id = Guid.NewGuid(), Email = email, Owner = owner };
}

public sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options)
    : DbContext(options), ITenantScoped
{
    public DbSet<Ledger> Ledgers => Set<Ledger>();

    public DbSet<Depot> Depots => Set<Depot>();

    public DbSet<Member> Members => Set<Member>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ledger>().ToTable("Ledger", "tenancy");
        modelBuilder.Entity<Depot>().ToTable("Depots", "tenancy");

        modelBuilder.Entity<Member>(b =>
        {
            b.ToTable("Member", "tenancy");

            // Deliberately over the NULLABLE column, which is what Rask.Auth does NOT do. Pinning the
            // divergence is the point: this is the index TenantKey exists to avoid.
            b.HasIndex(m => new { m.Owner, m.Email }).IsUnique();
        });

        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>
/// Tenancy against every engine Rask supports. The filter's SQL and the behaviour of NULL inside a unique
/// index are both provider-specific, and the second is the reason <c>TenantKey</c> exists at all.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresTenancyTests
{
    private const string Schema = "tenancy";
    private readonly Guid _acme = Guid.NewGuid();
    private readonly Guid _globex = Guid.NewGuid();

    [Fact]
    public async Task A_tenant_sees_only_its_own_rows_and_Across_sees_all()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);

        await using (var db = NewContext())
        {
            await Postgres.ResetSchemaAsync(db, Schema);

            using (Tenant.Use(_acme))
            {
                db.Add(Ledger.For("ACME-1"));
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            using (Tenant.Use(_globex))
            {
                db.Add(Ledger.For("GLOBEX-1"));
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }

        await using (var db = NewContext())
        {
            using (Tenant.Use(_acme))
            {
                Assert.Equal(["ACME-1"], await db.Ledgers.Select(l => l.Reference).ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
            }

            using (Tenant.Use(_globex))
            {
                Assert.Equal(["GLOBEX-1"], await db.Ledgers.Select(l => l.Reference).ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
            }

            using (Tenant.Across())
            {
                Assert.Equal(2, await db.Ledgers.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
            }
        }

        await using (var drop = NewContext())
        {
            await Postgres.DropSchemaAsync(drop, Schema);
        }
    }

    [Fact]
    public async Task Two_nulls_in_a_unique_index_are_distinct_here_which_is_why_TenantKey_exists()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);

        await using var db = NewContext();
        await Postgres.ResetSchemaAsync(db, Schema);

        try
        {
            // PostgreSQL treats two NULLs as distinct, so BOTH rows are accepted — meaning a unique index
            // over a nullable tenant would let any number of administrators share one address. SQL Server
            // treats them as equal and would refuse the second. Same schema, opposite outcomes; Rask.Auth
            // indexes TenantKey, which folds the null to Guid.Empty, so neither behaviour applies.
            using (Tenant.Across())
            {
                db.Add(Member.For("root@example.com", owner: null));
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);

                db.Add(Member.For("root@example.com", owner: null));
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);

                Assert.Equal(2, await db.Members.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
            }
        }
        finally
        {
            await Postgres.DropSchemaAsync(db, Schema);
        }
    }

    // The interceptors are wired the way an application wires them — AddRaskData registers them and the
    // context adds them. Without that nothing stamps TenantId, every insert stores null, and the rows become
    // invisible to every tenant: the same shape as forgetting them and losing CreatedAt.
    private static TenancyDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TenancyDbContext>()
            .UseRaskPostgresAt(Postgres.Required)
            .AddInterceptors(new AuditingInterceptor(TimeProvider.System))
            .Options);
}

/// <summary>
/// SQL Server, proved without a server: the filter is in the model and translates. Execution needs a host
/// that can run the image, which Apple Silicon cannot.
/// </summary>
public sealed class SqlServerTenancyTests
{
    private const string Offline = "Server=(local);Database=rask_translation_only;Trusted_Connection=True";

    [Fact]
    public void The_tenant_filter_is_in_the_model_and_translates()
    {
        using var db = new TenancyDbContext(
            new DbContextOptionsBuilder<TenancyDbContext>().UseRaskSqlServerAt(Offline).Options);
        var ledger = db.Model.FindEntityType(typeof(Ledger))!;

        Assert.NotNull(ledger.FindProperty(Columns.TenantId));

        using (Tenant.Use(Guid.NewGuid()))
        {
            var sql = db.Ledgers.ToQueryString();

            // Parameterised, not inlined: a literal here would mean the first tenant's id was burned into
            // the cached model for every tenant after it.
            Assert.Contains("TenantId", sql, StringComparison.Ordinal);
            Assert.Contains("@", sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_numbered_tenant_is_an_int_column_compared_with_an_int_parameter()
    {
        using var db = new TenancyDbContext(
            new DbContextOptionsBuilder<TenancyDbContext>().UseRaskSqlServerAt(Offline).Options);

        string sql;
        using (Tenant.Use(987654))
        {
            sql = db.Depots.ToQueryString();
        }

        Assert.Equal("int", db.Model.FindEntityType(typeof(Depot))!.FindProperty(Columns.TenantId)!.GetColumnType());
        Assert.Contains(" int = 987654;", sql, StringComparison.Ordinal);
        Assert.Contains("[d].[TenantId] = @", sql, StringComparison.Ordinal);
    }
}
