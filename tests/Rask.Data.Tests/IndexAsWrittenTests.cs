using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Core.Forms;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Data.Tests;

/// <summary>
/// A tenant's users, the way an existing app has them: a text key, a <see cref="Guid" /> tenant kept as text, a
/// sign-in name that is one user's among EVERY tenant, and a badge number that is unique within one.
/// </summary>
public sealed class Clerk : Aggregate<string>, IFormModel, IPerTenant
{
    public const string UsernameTaken = "Ez a felhasználónév foglalt.";
    public const string BadgeTaken = "This badge is already handed out.";
    public const string UsernameKey = "Clerk_username_key";
    public const string BadgeKey = "Clerk_badge_key";

    private Clerk() { }

    public Guid? TenantId { get; private set; }

    public string Username { get; private set; } = "";

    public string? Badge { get; private set; }

    public static Clerk Named(string username, string? badge = null) =>
        new() { Id = Guid.NewGuid().ToString("N"), Username = username, Badge = badge };

    public static void Configure(EntityTypeBuilder<Clerk> builder)
    {
        builder.ToTable("Clerk");
        builder.Property(c => c.TenantId).HasConversion<string>().HasMaxLength(64);
        builder.Property(c => c.Username).HasMaxLength(255);

        builder.HasIndex(c => c.Username).HasDatabaseName(UsernameKey).IsUnique(UsernameTaken);
        builder.HasIndex(c => new { c.TenantId, c.Badge }).HasDatabaseName(BadgeKey).IsUnique(BadgeTaken);
    }
}

/// <summary>
/// The index on a tenant-scoped table is what the app wrote: without <c>TenantId</c> it is unique among every
/// tenant's rows (TA's sign-in name), with it within one tenant (an Eszaf-style badge). Nothing adds the
/// tenant, and the name, message and filter the app gave an index are kept as written.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class IndexAsWrittenTests : IDisposable
{
    private static readonly Guid Acme = new("0f9d5a3c-7c1e-4d52-9a54-2b6f1f0c8e11");
    private static readonly Guid Globex = new("a1b2c3d4-0000-4000-8000-00000000beef");

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-across-tenants-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task An_index_without_the_tenant_is_left_exactly_as_written()
    {
        await using var database = await StartDatabaseAsync();

        var index = IndexNamed(database, Clerk.UsernameKey);

        Assert.Equal(["Username"], index.Properties.Select(p => p.Name));
        Assert.True(index.IsUnique);
        Assert.Equal(Clerk.UsernameTaken, index.FindAnnotation(UniqueViolation.Annotation)?.Value);
    }

    [Fact]
    public async Task An_index_that_names_the_tenant_keeps_its_order_its_name_and_its_message()
    {
        await using var database = await StartDatabaseAsync();

        var index = IndexNamed(database, Clerk.BadgeKey);

        Assert.Equal(["TenantId", "Badge"], index.Properties.Select(p => p.Name));
        Assert.True(index.IsUnique);
        Assert.Equal(Clerk.BadgeTaken, index.FindAnnotation(UniqueViolation.Annotation)?.Value);
    }

    [Fact]
    public async Task A_username_another_tenant_holds_fails_with_the_message_under_that_field()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Clerk.Named("ada"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => SaveAsync(Globex, Clerk.Named("ada")));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Clerk.UsernameTaken, failure.Message);
        Assert.Equal(["Username"], failure.Fields);
        Assert.Equal(Clerk.UsernameKey, failure.Source);
        Assert.IsType<DbUpdateException>(refused.InnerException);
    }

    [Fact]
    public async Task A_badge_is_unique_within_a_tenant_and_reported_by_the_name_it_was_given()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Clerk.Named("ada", badge: "B-1"));
        await SaveAsync(Globex, Clerk.Named("bob", badge: "B-1"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => SaveAsync(Acme, Clerk.Named("cyd", badge: "B-1")));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Clerk.BadgeTaken, failure.Message);
        Assert.Equal(Clerk.BadgeKey, failure.Source);
    }

    [Fact]
    public async Task The_form_is_told_a_username_another_tenant_holds_is_taken()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Globex, Clerk.Named("ada", badge: "B-1"));

        var taken = await CheckAsync(Acme, new ClerkModel { Username = "ada", Badge = "B-1" }, "Username");
        var badge = await CheckAsync(Acme, new ClerkModel { Username = "ada", Badge = "B-1" }, "Badge");
        var free = await CheckAsync(Acme, new ClerkModel { Username = "bob" }, null);

        var failure = Assert.Single(taken);
        Assert.Equal(Clerk.UsernameTaken, failure.Message);
        Assert.Equal(Clerk.UsernameKey, failure.Source);
        Assert.Empty(badge);
        Assert.Empty(free);
    }

    [Fact]
    public async Task The_form_of_somebody_with_no_tenant_is_told_too()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Globex, Clerk.Named("ada"));

        IReadOnlyList<FieldFailure> across;
        using (Tenant.Across())
        {
            across = await Rules()!.Check(new ClerkModel { Username = "ada" }, null, TestContext.Current.CancellationToken);
        }

        Assert.Equal(Clerk.UsernameTaken, Assert.Single(across).Message);
    }

    private static IIndex IndexNamed(TestDatabase database, string name) =>
        database.Context.Model.FindEntityType(typeof(Clerk))!.GetIndexes().Single(i => i.GetDatabaseName() == name);

    private static async Task<IReadOnlyList<FieldFailure>> CheckAsync(Guid tenant, ClerkModel model, string? field)
    {
        using (Tenant.Use(tenant))
        {
            return await Rules()!.Check(model, field, TestContext.Current.CancellationToken);
        }
    }

    // What the generator registered for ClerkModel, reached the only way a file-local class can be: by name.
    private static IStoreRules? Rules()
    {
        var rules = typeof(ClerkModel).Assembly.GetTypes().Single(t => t.Name.EndsWith("__ClerkModelRules", StringComparison.Ordinal));
        var method = rules.GetMethod("StoreRules", BindingFlags.Static | BindingFlags.NonPublic)!;
        return method.CreateDelegate<Func<IServiceProvider?, IStoreRules?>>()(null);
    }

    private static async Task SaveAsync(Guid tenant, Clerk clerk)
    {
        using (Tenant.Use(tenant))
        {
            await clerk.Save(cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
