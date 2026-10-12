using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Providers.E2E.Tests;

/// <summary>A plain EF Core entity: the message is the index's, and needs nothing else of Rask's.</summary>
public sealed class Subscriber
{
    public const string EmailTaken = "This address is already subscribed.";
    public const string SeatTaken = "That seat in that room is already taken.";

    public int Id { get; set; }

    public string Email { get; set; } = "";

    public string Room { get; set; } = "";

    public int Seat { get; set; }

    public string Nickname { get; set; } = "";
}

public sealed class SubscriberDbContext(DbContextOptions<SubscriberDbContext> options) : DbContext(options)
{
    public const string Schema = "violations";

    public DbSet<Subscriber> Subscribers => Set<Subscriber>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Subscriber>(b =>
        {
            b.ToTable("Subscriber", Schema);
            b.HasIndex(s => s.Email).IsUnique(Subscriber.EmailTaken);
            b.HasIndex(s => new { s.Room, s.Seat }).IsUnique(Subscriber.SeatTaken);
            b.HasIndex(s => s.Nickname).IsUnique();
        });
}

/// <summary>
/// A unique index's message on PostgreSQL: the violated index is the constraint SQLSTATE 23505 names.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresViolationMessageTests
{
    [Fact]
    public async Task A_duplicate_in_a_single_column_index_fails_with_the_message_under_that_field()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var app = App();
        await using var db = await ResetAsync(app);
        db.Add(new Subscriber { Email = "ada@example.com", Room = "A", Seat = 1, Nickname = "ada" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Add(new Subscriber { Email = "ada@example.com", Room = "A", Seat = 2, Nickname = "ada2" });
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal([Subscriber.EmailTaken], refused.Errors["Email"]);
        Assert.Single(refused.Errors);
    }

    [Fact]
    public async Task A_duplicate_in_a_composite_index_fails_once_with_the_message_over_both_fields()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var app = App();
        await using var db = await ResetAsync(app);
        db.Add(new Subscriber { Email = "ada@example.com", Room = "A", Seat = 1, Nickname = "ada" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Add(new Subscriber { Email = "bob@example.com", Room = "A", Seat = 1, Nickname = "bob" });
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(["Room", "Seat"], Assert.Single(refused.Failures).Fields);
        Assert.Equal([Subscriber.SeatTaken], refused.Errors["Room"]);
        Assert.Equal([Subscriber.SeatTaken], refused.Errors["Seat"]);
    }

    [Fact]
    public async Task An_index_with_no_message_keeps_the_providers_own_error()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var app = App();
        await using var db = await ResetAsync(app);
        db.Add(new Subscriber { Email = "ada@example.com", Room = "A", Seat = 1, Nickname = "ada" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Add(new Subscriber { Email = "bob@example.com", Room = "A", Seat = 2, Nickname = "ada" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<SubscriberDbContext> ResetAsync(ServiceProvider app)
    {
        var db = await app.GetRequiredService<IDbContextFactory<SubscriberDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        await Postgres.ResetSchemaAsync(db, SubscriberDbContext.Schema);
        return db;
    }

    // Wired the way an application wires it: AddRaskData registers the interceptors and the context adds them.
    private static ServiceProvider App()
    {
        var services = new ServiceCollection();
        services.AddRaskData();
        services.AddDbContextFactory<SubscriberDbContext>((sp, o) => o
            .UseRaskPostgresAt(Postgres.Required)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        return services.BuildServiceProvider();
    }
}

/// <summary>
/// A unique index's message on SQL Server, on a plain entity in a database of its own: the violated index is
/// the one error 2601 names.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerViolationMessageTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_violations";
    private const int UniqueIndexViolated = 2601;
    private const int UniqueConstraintViolated = 2627;

    public async ValueTask InitializeAsync()
    {
        if (!SqlServer.Available)
        {
            return;
        }

        await using var app = App();
        await using var db = await app.GetRequiredService<IDbContextFactory<SubscriberDbContext>>().CreateDbContextAsync();
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
            await using var db = await app.GetRequiredService<IDbContextFactory<SubscriberDbContext>>().CreateDbContextAsync();
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task A_duplicate_in_a_single_column_index_fails_with_the_message_under_that_field()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await app.GetRequiredService<IDbContextFactory<SubscriberDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

        db.Add(new Subscriber { Email = "ada@example.com", Room = "A", Seat = 2, Nickname = "ada2" });
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal([Subscriber.EmailTaken], refused.Errors["Email"]);
        Assert.Single(refused.Errors);
        Assert.Equal(UniqueIndexViolated, ProviderErrorOf(refused));
    }

    [Fact]
    public async Task A_duplicate_in_a_unique_constraint_of_the_same_name_fails_with_the_message_too()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await app.GetRequiredService<IDbContextFactory<SubscriberDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

        // A schema somebody else wrote may hold the rule as a UNIQUE constraint rather than a unique index.
        // SQL Server then reports error 2627 and a differently worded message, naming the same object.
        await db.Database.ExecuteSqlAsync(
            $"""
             DROP INDEX [IX_Subscriber_Email] ON [violations].[Subscriber];
             ALTER TABLE [violations].[Subscriber] ADD CONSTRAINT [IX_Subscriber_Email] UNIQUE ([Email]);
             """,
            TestContext.Current.CancellationToken);

        db.Add(new Subscriber { Email = "ada@example.com", Room = "A", Seat = 2, Nickname = "ada2" });
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal([Subscriber.EmailTaken], refused.Errors["Email"]);
        Assert.Equal(UniqueConstraintViolated, ProviderErrorOf(refused));
    }

    [Fact]
    public async Task A_duplicate_in_a_composite_index_fails_once_with_the_message_over_both_fields()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await app.GetRequiredService<IDbContextFactory<SubscriberDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

        db.Add(new Subscriber { Email = "bob@example.com", Room = "A", Seat = 1, Nickname = "bob" });
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(["Room", "Seat"], Assert.Single(refused.Failures).Fields);
        Assert.Equal([Subscriber.SeatTaken], refused.Errors["Room"]);
        Assert.Equal([Subscriber.SeatTaken], refused.Errors["Seat"]);
    }

    [Fact]
    public async Task A_value_that_spells_another_indexs_name_does_not_change_which_message_is_given()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await app.GetRequiredService<IDbContextFactory<SubscriberDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        const string Crafted = "x', 'IX_Subscriber_Room_Seat";
        db.Add(new Subscriber { Email = Crafted, Room = "B", Seat = 1, Nickname = "c1" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // SQL Server appends the duplicate key value to its message, quotes and all.
        db.Add(new Subscriber { Email = Crafted, Room = "B", Seat = 2, Nickname = "c2" });
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal([Subscriber.EmailTaken], refused.Errors["Email"]);
    }

    [Fact]
    public async Task An_index_with_no_message_keeps_the_providers_own_error()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await app.GetRequiredService<IDbContextFactory<SubscriberDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

        db.Add(new Subscriber { Email = "bob@example.com", Room = "A", Seat = 3, Nickname = "ada" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    // The number SQL Server gave the failure Rask translated: DbUpdateException, then the provider's own.
    private static int ProviderErrorOf(RaskValidationException refused) =>
        ((Microsoft.Data.SqlClient.SqlException)refused.InnerException!.InnerException!).Number;

    private static ServiceProvider App()
    {
        var services = new ServiceCollection();
        services.AddRaskData();
        services.AddDbContextFactory<SubscriberDbContext>((sp, o) => o
            .UseSqlServer(SqlServer.Database(DatabaseName))
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        return services.BuildServiceProvider();
    }
}

/// <summary>Referenced by a mapped table, and by one the model does not know: a sentence for each.</summary>
public sealed class Truck
{
    public const string OnAHaul = "The truck is on a haul and cannot be deleted.";
    public const string InUse = "The truck is still in use and cannot be deleted.";

    public int Id { get; set; }

    public string Plate { get; set; } = "";
}

public sealed class Haul
{
    public int Id { get; set; }

    public int TruckId { get; set; }
}

/// <summary>Referenced, and says nothing about it.</summary>
public sealed class Hangar
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public sealed class Plane
{
    public int Id { get; set; }

    public int HangarId { get; set; }
}

public sealed class FleetDbContext(DbContextOptions<FleetDbContext> options) : DbContext(options)
{
    public const string Schema = "referenced";
    public const string HaulsTruck = "FK_Haul_Truck_TruckId";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Truck>(b =>
        {
            b.ToTable("Truck", Schema);
            b.HasMany<Haul>().WithOne().HasForeignKey(t => t.TruckId).OnDelete(DeleteBehavior.Restrict, Truck.OnAHaul);
            b.WhenReferenced(Truck.InUse);
        });
        modelBuilder.Entity<Haul>().ToTable("Haul", Schema);

        modelBuilder.Entity<Hangar>(b =>
        {
            b.ToTable("Hangar", Schema);
            b.HasMany<Plane>().WithOne().HasForeignKey(p => p.HangarId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Plane>().ToTable("Plane", Schema);
    }
}

/// <summary>
/// A refused delete's sentence on PostgreSQL: the relationship is the constraint SQLSTATE 23503 names, and a
/// constraint the model does not know is answered by the entity.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresReferencedDeleteTests
{
    [Fact]
    public async Task Deleting_a_row_a_mapped_row_references_fails_with_the_relationships_sentence()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var app = App();
        await using var db = await ResetAsync(app);
        var truck = await TruckAsync(db);
        db.Add(new Haul { TruckId = truck.Id });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Remove(truck);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Truck.OnAHaul, failure.Message);
        Assert.Empty(failure.Fields);
        Assert.Equal(FleetDbContext.HaulsTruck, failure.Source);
    }

    [Fact]
    public async Task Deleting_a_row_only_an_unmapped_table_references_fails_with_the_entitys_sentence()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var app = App();
        await using var db = await ResetAsync(app);
        var truck = await TruckAsync(db);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE referenced."Inspection" (
                "Id" serial PRIMARY KEY,
                "TruckId" integer NOT NULL CONSTRAINT "FK_Inspection_Truck" REFERENCES referenced."Truck" ("Id"));
            """,
            TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"""INSERT INTO referenced."Inspection" ("TruckId") VALUES ({truck.Id});""",
            TestContext.Current.CancellationToken);

        db.Remove(truck);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Truck.InUse, failure.Message);
        Assert.Equal("FK_Inspection_Truck", failure.Source);
    }

    [Fact]
    public async Task A_relationship_with_no_sentence_keeps_the_providers_own_error()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var app = App();
        await using var db = await ResetAsync(app);
        var hangar = new Hangar { Name = "North" };
        db.Add(hangar);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Add(new Plane { HangarId = hangar.Id });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Remove(hangar);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_row_inserted_with_a_key_that_points_nowhere_keeps_the_providers_own_error()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var app = App();
        await using var db = await ResetAsync(app);

        db.Add(new Haul { TruckId = 404 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<Truck> TruckAsync(FleetDbContext db)
    {
        var truck = new Truck { Plate = "ABC-123" };
        db.Add(truck);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return truck;
    }

    private static async Task<FleetDbContext> ResetAsync(ServiceProvider app)
    {
        var db = await app.GetRequiredService<IDbContextFactory<FleetDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        await Postgres.ResetSchemaAsync(db, FleetDbContext.Schema);
        return db;
    }

    private static ServiceProvider App()
    {
        var services = new ServiceCollection();
        services.AddRaskData();
        services.AddDbContextFactory<FleetDbContext>((sp, o) => o
            .UseRaskPostgresAt(Postgres.Required)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        return services.BuildServiceProvider();
    }
}

/// <summary>
/// A refused delete's sentence on SQL Server, in a database of its own: the relationship is the constraint
/// error 547 quotes, and a constraint the model does not know is answered by the entity.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerReferencedDeleteTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_referenced";
    private const int ConstraintConflict = 547;

    public async ValueTask InitializeAsync()
    {
        if (!SqlServer.Available)
        {
            return;
        }

        await using var app = App();
        await using var db = await app.GetRequiredService<IDbContextFactory<FleetDbContext>>().CreateDbContextAsync();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await using var app = App();
            await using var db = await app.GetRequiredService<IDbContextFactory<FleetDbContext>>().CreateDbContextAsync();
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Deleting_a_row_a_mapped_row_references_fails_with_the_relationships_sentence()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await OpenAsync(app);
        var truck = await TruckAsync(db);
        db.Add(new Haul { TruckId = truck.Id });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Remove(truck);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Truck.OnAHaul, failure.Message);
        Assert.Empty(failure.Fields);
        Assert.Equal(FleetDbContext.HaulsTruck, failure.Source);
        Assert.Equal(ConstraintConflict, ProviderErrorOf(refused));
    }

    [Fact]
    public async Task Deleting_a_row_only_an_unmapped_table_references_fails_with_the_entitys_sentence()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await OpenAsync(app);
        var truck = await TruckAsync(db);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [referenced].[Inspection] (
                [Id] int IDENTITY PRIMARY KEY,
                [TruckId] int NOT NULL CONSTRAINT [FK_Inspection_Truck] REFERENCES [referenced].[Truck] ([Id]));
            """,
            TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO [referenced].[Inspection] ([TruckId]) VALUES ({truck.Id});",
            TestContext.Current.CancellationToken);

        db.Remove(truck);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Truck.InUse, failure.Message);
        Assert.Equal(nameof(Truck), failure.Source);
        Assert.Equal(ConstraintConflict, ProviderErrorOf(refused));
    }

    [Fact]
    public async Task A_relationship_with_no_sentence_keeps_the_providers_own_error()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await OpenAsync(app);
        var hangar = new Hangar { Name = "North" };
        db.Add(hangar);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Add(new Plane { HangarId = hangar.Id });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Remove(hangar);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_row_inserted_with_a_key_that_points_nowhere_keeps_the_providers_own_error()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = App();
        await using var db = await OpenAsync(app);

        db.Add(new Haul { TruckId = 404 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<Truck> TruckAsync(FleetDbContext db)
    {
        var truck = new Truck { Plate = "ABC-123" };
        db.Add(truck);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return truck;
    }

    private static Task<FleetDbContext> OpenAsync(ServiceProvider app) =>
        app.GetRequiredService<IDbContextFactory<FleetDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

    // The number SQL Server gave the failure Rask translated: DbUpdateException, then the provider's own.
    private static int ProviderErrorOf(RaskValidationException refused) =>
        ((Microsoft.Data.SqlClient.SqlException)refused.InnerException!.InnerException!).Number;

    private static ServiceProvider App()
    {
        var services = new ServiceCollection();
        services.AddRaskData();
        services.AddDbContextFactory<FleetDbContext>((sp, o) => o
            .UseSqlServer(SqlServer.Database(DatabaseName))
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        return services.BuildServiceProvider();
    }
}
