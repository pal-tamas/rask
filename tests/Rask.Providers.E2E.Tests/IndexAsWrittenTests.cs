using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Providers.E2E.Tests;

/// <summary>A tenant's sign-ins: a name unique among every tenant, a badge unique within one, as the app wrote them.</summary>
public sealed class Login : Aggregate<string>, IFormModel, IPerTenant
{
    public const string UsernameTaken = "Ez a felhasználónév foglalt.";
    public const string BadgeTaken = "This badge is already handed out.";
    public const string UsernameKey = "Login_username_key";
    public const string BadgeKey = "Login_badge_key";

    private Login() { }

    public Guid? TenantId { get; private set; }

    public string Username { get; private set; } = "";

    public string? Badge { get; private set; }

    public static Login Named(string username, string? badge = null) =>
        new() { Id = Guid.NewGuid().ToString("N"), Username = username, Badge = badge };
}

/// <summary>A context of the app's own, the way TA's is: the tenant as text, the index names its database has.</summary>
public sealed class LoginDbContext(DbContextOptions<LoginDbContext> options) : DbContext(options), ITenantScoped
{
    public const string Schema = "across";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Login>(b =>
        {
            b.ToTable("Login", Schema);
            b.Property(l => l.TenantId).HasConversion<string>().HasMaxLength(64);
            b.Property(l => l.Username).HasMaxLength(255);
            b.Property(l => l.Badge).HasMaxLength(32);
            b.HasIndex(l => l.Username).HasDatabaseName(Login.UsernameKey).IsUnique(Login.UsernameTaken);
            b.HasIndex(l => new { l.TenantId, l.Badge }).HasDatabaseName(Login.BadgeKey).IsUnique(Login.BadgeTaken);
        });

        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>
/// The violation of an index without the tenant, and of one that names it, recognised by the name it was given on
/// PostgreSQL.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresIndexAsWrittenTests
{
    private readonly Guid _acme = Guid.NewGuid();
    private readonly Guid _globex = Guid.NewGuid();

    [Fact]
    public async Task A_username_another_tenant_holds_fails_with_the_message_by_the_indexs_name()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var app = IndexAsWrittenApp.Build(o => o.UseRaskPostgresAt(Postgres.Required));
        await using var db = await ResetAsync(app);
        await IndexAsWrittenApp.SaveAsync(db, _acme, Login.Named("ada"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => IndexAsWrittenApp.SaveAsync(db, _globex, Login.Named("ada")));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Login.UsernameTaken, failure.Message);
        Assert.Equal(["Username"], failure.Fields);
        Assert.Equal(Login.UsernameKey, failure.Source);
    }

    [Fact]
    public async Task A_badge_taken_within_the_tenant_fails_with_the_message_by_the_name_it_was_given()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var app = IndexAsWrittenApp.Build(o => o.UseRaskPostgresAt(Postgres.Required));
        await using var db = await ResetAsync(app);
        await IndexAsWrittenApp.SaveAsync(db, _acme, Login.Named("ada", badge: "B-1"));
        await IndexAsWrittenApp.SaveAsync(db, _globex, Login.Named("bob", badge: "B-1"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => IndexAsWrittenApp.SaveAsync(db, _acme, Login.Named("cyd", badge: "B-1")));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Login.BadgeTaken, failure.Message);
        Assert.Equal(Login.BadgeKey, failure.Source);
    }

    private static async Task<LoginDbContext> ResetAsync(ServiceProvider app)
    {
        var db = await app.GetRequiredService<IDbContextFactory<LoginDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        await Postgres.ResetSchemaAsync(db, LoginDbContext.Schema);
        return db;
    }
}

/// <summary>
/// The same on SQL Server, in a database of its own — and a database whose index is GLOBAL while the model's
/// names the tenant.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerIndexAsWrittenTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_across_tenants";
    private const int UniqueConstraintViolated = 2627;

    private readonly Guid _acme = Guid.NewGuid();
    private readonly Guid _globex = Guid.NewGuid();

    public async ValueTask InitializeAsync()
    {
        if (!SqlServer.Available)
        {
            return;
        }

        await using var app = App();
        await using var db = await app.GetRequiredService<IDbContextFactory<LoginDbContext>>().CreateDbContextAsync();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await using var app = App();
            await using var db = await app.GetRequiredService<IDbContextFactory<LoginDbContext>>().CreateDbContextAsync();
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public void The_index_that_names_the_tenant_keeps_its_name_and_its_null_filter_names_both_columns()
    {
        using var db = new LoginDbContext(new DbContextOptionsBuilder<LoginDbContext>()
            .UseSqlServer("Server=(local);Database=rask_model_only;Trusted_Connection=True").Options);
        var indexes = db.Model.FindEntityType(typeof(Login))!.GetIndexes().ToList();

        var badge = indexes.Single(i => i.GetDatabaseName() == Login.BadgeKey);
        var username = indexes.Single(i => i.GetDatabaseName() == Login.UsernameKey);

        Assert.Equal(["TenantId", "Badge"], badge.Properties.Select(p => p.Name));
        Assert.Equal("[TenantId] IS NOT NULL AND [Badge] IS NOT NULL", badge.GetFilter());
        Assert.Equal(["Username"], username.Properties.Select(p => p.Name));
    }

    [Fact]
    public async Task A_username_another_tenant_holds_fails_with_the_message_by_the_indexs_name()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await OpenAsync(app);
        await IndexAsWrittenApp.SaveAsync(db, _acme, Login.Named("ada"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => IndexAsWrittenApp.SaveAsync(db, _globex, Login.Named("ada")));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Login.UsernameTaken, failure.Message);
        Assert.Equal(Login.UsernameKey, failure.Source);
    }

    [Fact]
    public async Task A_badge_taken_within_the_tenant_fails_with_the_message_by_the_name_it_was_given()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await OpenAsync(app);
        await IndexAsWrittenApp.SaveAsync(db, _acme, Login.Named("ada", badge: "B-1"));
        await IndexAsWrittenApp.SaveAsync(db, _globex, Login.Named("bob", badge: "B-1"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => IndexAsWrittenApp.SaveAsync(db, _acme, Login.Named("cyd", badge: "B-1")));

        Assert.Equal(Login.BadgeKey, Assert.Single(refused.Failures).Source);
    }

    [Fact]
    public async Task A_global_constraint_under_the_name_the_model_kept_is_recognised()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await OpenAsync(app);

        // The schema an existing app has: the badge unique among EVERY tenant's rows, as a constraint.
        await db.Database.ExecuteSqlRawAsync(
            """
            DROP INDEX [Login_badge_key] ON [across].[Login];
            ALTER TABLE [across].[Login] ADD CONSTRAINT [Login_badge_key] UNIQUE ([Badge]);
            """,
            TestContext.Current.CancellationToken);
        await IndexAsWrittenApp.SaveAsync(db, _acme, Login.Named("eve", badge: "G-1"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => IndexAsWrittenApp.SaveAsync(db, _globex, Login.Named("fay", badge: "G-1")));

        Assert.Equal(Login.BadgeTaken, Assert.Single(refused.Failures).Message);
        Assert.Equal(
            UniqueConstraintViolated,
            ((Microsoft.Data.SqlClient.SqlException)refused.InnerException!.InnerException!).Number);
    }

    private static Task<LoginDbContext> OpenAsync(ServiceProvider app) =>
        app.GetRequiredService<IDbContextFactory<LoginDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

    private static ServiceProvider App() =>
        IndexAsWrittenApp.Build(o => o.UseSqlServer(SqlServer.Database(DatabaseName)));
}

/// <summary>Wired the way an application wires it: AddRaskData registers the interceptors, the context adds them.</summary>
internal static class IndexAsWrittenApp
{
    internal static ServiceProvider Build(Action<DbContextOptionsBuilder> provider)
    {
        var services = new ServiceCollection();
        services.AddRaskData();
        services.AddDbContextFactory<LoginDbContext>((sp, o) =>
        {
            provider(o);
            o.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });
        return services.BuildServiceProvider();
    }

    internal static async Task SaveAsync(LoginDbContext db, Guid tenant, Login login)
    {
        using (Tenant.Use(tenant))
        {
            db.Add(login);
            try
            {
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }
}
