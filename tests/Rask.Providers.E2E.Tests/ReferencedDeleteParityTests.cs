using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Providers.E2E.Tests;

/// <summary>Referenced three ways: two relationships with a sentence each, one with none, and the entity's own.</summary>
public sealed class Quay
{
    public const string OnABin = "The quay still has a bin.";
    public const string OnADock = "The quay still has a dock.";
    public const string InUse = "The quay is still in use.";

    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public sealed class Bin
{
    public int Id { get; set; }

    public int QuayId { get; set; }
}

public sealed class Dock
{
    public int Id { get; set; }

    public int QuayId { get; set; }
}

public sealed class Ramp
{
    public int Id { get; set; }

    public int QuayId { get; set; }
}

public sealed class QuayDbContext(DbContextOptions<QuayDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_quay_refs";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (string.Equals(Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            modelBuilder.HasDefaultSchema(Schema);
        }

        modelBuilder.Entity<Quay>(b =>
        {
            b.HasMany<Bin>().WithOne().HasForeignKey(x => x.QuayId).OnDelete(DeleteBehavior.Restrict, Quay.OnABin);
            b.HasMany<Dock>().WithOne().HasForeignKey(x => x.QuayId).OnDelete(DeleteBehavior.Restrict, Quay.OnADock);
            b.HasMany<Ramp>().WithOne().HasForeignKey(x => x.QuayId).OnDelete(DeleteBehavior.Restrict);
            b.WhenReferenced(Quay.InUse);
        });
        modelBuilder.Entity<Bin>();
        modelBuilder.Entity<Dock>();
        modelBuilder.Entity<Ramp>();
    }
}

/// <summary>
///     A refused delete says the same sentence on every provider — the relationship that still holds a row, though
///     SQLite names no constraint — which is what lets an app run its tests on SQLite alone.
/// </summary>
public abstract class ReferencedDeleteParityTests : IAsyncLifetime
{
    private protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    private protected abstract bool Available { get; }

    private protected abstract string SkipReason { get; }

    private protected abstract DbContextOptionsBuilder<QuayDbContext> Provider(DbContextOptionsBuilder<QuayDbContext> options);

    private protected abstract Task Recreate(DbContext db);

    private protected abstract Task Drop(DbContext db);

    public async ValueTask InitializeAsync()
    {
        if (Available)
        {
            await using var db = NewContext();
            await Recreate(db);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Available)
        {
            await using var db = NewContext();
            await Drop(db);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task The_second_of_two_relationships_with_a_sentence_answers_when_it_holds_the_row()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();
        var quay = await QuayAsync(db, d => new Dock { QuayId = d });

        db.Remove(quay);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => db.SaveChangesAsync(Ct));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Quay.OnADock, failure.Message);
        Assert.Equal("FK_Dock_Quay_QuayId", failure.Source);
    }

    [Fact]
    public async Task A_relationship_with_no_sentence_is_answered_by_the_entitys_sentence()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();
        var quay = await QuayAsync(db, d => new Ramp { QuayId = d });

        db.Remove(quay);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => db.SaveChangesAsync(Ct));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Quay.InUse, failure.Message);
        Assert.Equal("FK_Ramp_Quay_QuayId", failure.Source);
    }

    [Fact]
    public async Task An_execute_delete_is_answered_with_the_same_sentence()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();
        var quay = await QuayAsync(db, d => new Bin { QuayId = d });

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => db.Set<Quay>().Where(d => d.Id == quay.Id).ExecuteDeleteAsync(Ct));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Quay.OnABin, failure.Message);
        Assert.Equal("FK_Bin_Quay_QuayId", failure.Source);
    }

    // A quay, and one row that references it — saved, then forgotten, as a request of its own would find them.
    private static async Task<Quay> QuayAsync(QuayDbContext db, Func<int, object> referencing)
    {
        var quay = new Quay { Name = "North" };
        db.Add(quay);
        await db.SaveChangesAsync(Ct);
        db.Add(referencing(quay.Id));
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        db.Attach(quay);
        return quay;
    }

    private QuayDbContext NewContext() =>
        new(Provider(new DbContextOptionsBuilder<QuayDbContext>()).AddInterceptors(Interceptors()).Options);

    // Wired the way an application wires it: AddRaskData registers the interceptors and the context adds them.
    private static List<ISaveChangesInterceptor> Interceptors()
    {
        var services = new ServiceCollection();
        services.AddRaskData();
        using var provider = services.BuildServiceProvider();
        return [.. provider.GetServices<ISaveChangesInterceptor>()];
    }
}

public sealed class SqliteReferencedDeleteParityTests : ReferencedDeleteParityTests
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rask-e2e-quay-{Guid.NewGuid():N}.db");

    private protected override bool Available => true;

    private protected override string SkipReason => "";

    private protected override DbContextOptionsBuilder<QuayDbContext> Provider(DbContextOptionsBuilder<QuayDbContext> options) =>
        options.UseSqlite($"Data Source={_path}");

    private protected override Task Recreate(DbContext db) => db.Database.EnsureCreatedAsync(Ct);

    private protected override Task Drop(DbContext db)
    {
        SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
        File.Delete(_path);
        return Task.CompletedTask;
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerReferencedDeleteParityTests : ReferencedDeleteParityTests
{
    private const string DatabaseName = "rask_e2e_quay_refs";

    private protected override bool Available => SqlServer.Available;

    private protected override string SkipReason => SqlServer.SkipReason;

    private protected override DbContextOptionsBuilder<QuayDbContext> Provider(DbContextOptionsBuilder<QuayDbContext> options) =>
        options.UseSqlServer(SqlServer.Database(DatabaseName));

    private protected override async Task Recreate(DbContext db)
    {
        await db.Database.EnsureDeletedAsync(Ct);
        await db.Database.EnsureCreatedAsync(Ct);
    }

    private protected override Task Drop(DbContext db) => db.Database.EnsureDeletedAsync();
}

[Collection(PostgresCollection.Name)]
public sealed class PostgresReferencedDeleteParityTests : ReferencedDeleteParityTests
{
    private protected override bool Available => Postgres.Available;

    private protected override string SkipReason => Postgres.SkipReason;

    private protected override DbContextOptionsBuilder<QuayDbContext> Provider(DbContextOptionsBuilder<QuayDbContext> options) =>
        options.UseRaskPostgresAt(Postgres.Required);

    private protected override Task Recreate(DbContext db) => Postgres.ResetSchemaAsync(db, QuayDbContext.Schema);

    private protected override Task Drop(DbContext db) => Postgres.DropSchemaAsync(db, QuayDbContext.Schema);
}
