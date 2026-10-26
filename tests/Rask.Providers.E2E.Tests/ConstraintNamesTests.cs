using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Providers.E2E.Tests;

/// <summary>The subscribers again, under an app-wide naming rule — the one an existing Prisma schema follows.</summary>
public sealed class NamedSubscriberDbContext(DbContextOptions<NamedSubscriberDbContext> options) : DbContext(options)
{
    public const string NicknameTaken = "That nickname is taken.";

    internal static readonly string Table = "Subscriber" + new string('s', 115);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ConstraintNames(n => n.Kind switch
        {
            ConstraintKind.PrimaryKey => $"{n.Table}_pkey",
            ConstraintKind.ForeignKey => $"{n.Table}_{n.Columns[0]}_fkey",
            ConstraintKind.Unique => $"{n.Table}_{n.Joined}_key",
            ConstraintKind.Index => $"{n.Table}_{n.Joined}_idx",
            ConstraintKind.Default => $"{n.Table}_{n.Columns[0]}_df",
            _ => null,
        });

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Subscriber>(b =>
        {
            b.ToTable(Table, "named");
            b.HasIndex(s => s.Email).IsUnique(Subscriber.EmailTaken);
            b.HasIndex(s => s.Nickname).HasDatabaseName("subscriber_nickname_as_written").IsUnique(NicknameTaken);
            b.Property(s => s.Room).HasDefaultValue("lobby");
        });
}

/// <summary>
/// <c>ConstraintNames</c> on a real SQL Server: the names reach the schema, cut to its 128 characters, and a unique
/// index's message still finds the index under the name the rule gave it.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerConstraintNamesTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_constraint_names";

    public async ValueTask InitializeAsync()
    {
        if (!SqlServer.Available)
        {
            return;
        }

        await using var app = App();
        await using var db = await app.GetRequiredService<IDbContextFactory<NamedSubscriberDbContext>>().CreateDbContextAsync();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Add(new Subscriber { Email = "ada@example.com", Room = "A", Seat = 1, Nickname = "ada" });
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await using var app = App();
            await using var db = await app.GetRequiredService<IDbContextFactory<NamedSubscriberDbContext>>().CreateDbContextAsync();
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task The_schema_carries_the_rule_s_names_cut_to_128_characters()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await app.GetRequiredService<IDbContextFactory<NamedSubscriberDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

        var names = await db.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM sys.objects WHERE parent_object_id = OBJECT_ID('named.' + QUOTENAME({0})) "
                + "UNION SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('named.' + QUOTENAME({0})) AND name IS NOT NULL",
                NamedSubscriberDbContext.Table)
            .ToListAsync(TestContext.Current.CancellationToken);

        var table = NamedSubscriberDbContext.Table;
        Assert.Contains($"{table}_pkey"[..128], names);
        Assert.Contains($"{table}_Email_key"[..128], names);
        Assert.Contains($"{table}_Room_df"[..128], names);
        Assert.Contains("subscriber_nickname_as_written", names);
    }

    [Fact]
    public async Task A_duplicate_under_a_renamed_index_still_fails_with_its_message()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await app.GetRequiredService<IDbContextFactory<NamedSubscriberDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

        db.Add(new Subscriber { Email = "ada@example.com", Room = "A", Seat = 2, Nickname = "ada2" });
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal([Subscriber.EmailTaken], refused.Errors["Email"]);
    }

    [Fact]
    public async Task A_duplicate_under_a_name_the_entity_stated_still_fails_with_its_message()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await app.GetRequiredService<IDbContextFactory<NamedSubscriberDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

        db.Add(new Subscriber { Email = "bob@example.com", Room = "A", Seat = 2, Nickname = "ada" });
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal([NamedSubscriberDbContext.NicknameTaken], refused.Errors["Nickname"]);
    }

    private static ServiceProvider App()
    {
        var services = new ServiceCollection();
        services.AddRaskData();
        services.AddDbContextFactory<NamedSubscriberDbContext>((sp, o) => o
            .UseSqlServer(SqlServer.Database(DatabaseName))
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        return services.BuildServiceProvider();
    }
}
