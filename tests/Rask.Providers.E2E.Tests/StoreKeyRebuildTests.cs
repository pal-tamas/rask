using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Providers.E2E.Tests;

/// <summary>A queue row as a battery stored it before its key was the app's: numbered by the store.</summary>
public sealed class NumberedQueueRow
{
    public long Id { get; set; }

    public string Subject { get; set; } = "";

    public Guid? UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }
}

/// <summary>The same row today: a Guid key made before the insert.</summary>
public sealed class GuidQueueRow
{
    public Guid Id { get; set; }

    public string Subject { get; set; } = "";

    public Guid? UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }
}

/// <summary>
///     The queue before the rebuild: an identity key, with its Guids in SQL Server's native layout unless
///     <paramref name="ordered" /> — a database made before Rask stored them by time.
/// </summary>
public sealed class NumberedQueueContext(DbContextOptions<NumberedQueueContext> options, bool ordered) : DbContext(options)
{
    public const string Schema = "rask_key_rebuild";

    internal bool Ordered => ordered;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, QueueModelKey>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ModelRegistry.ApplyConventions(configurationBuilder);
        if (!ordered)
        {
            configurationBuilder.Conventions.Remove(typeof(SqlServerGuidOrderConvention));
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Database.IsSqlite() ? null : Schema);
        modelBuilder.Entity<NumberedQueueRow>(b =>
        {
            b.ToTable("QueueRows");
            b.Property(x => x.Subject).HasMaxLength(100);
            b.HasIndex(x => new { x.ProcessedAt, x.Id });
        });
    }
}

/// <summary>The queue after: a Guid key, marked for the rebuild, its Guids stored by time on SQL Server.</summary>
public sealed class GuidQueueContext(DbContextOptions<GuidQueueContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Database.IsSqlite() ? null : NumberedQueueContext.Schema);
        modelBuilder.Entity<GuidQueueRow>(b =>
        {
            b.ToTable("QueueRows");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Subject).HasMaxLength(100);
            b.HasIndex(x => new { x.ProcessedAt, x.Id });
            b.RebuildsStoreKey(nameof(GuidQueueRow.ProcessedAt), nameof(GuidQueueRow.CreatedAt));
        });
    }
}

internal sealed class QueueModelKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), ((NumberedQueueContext)context).Ordered, designTime);
}

/// <summary>
///     The rebuild of a store-numbered key into a Guid on a real server: a waiting row refuses it, and the processed rows
///     come back under ids made from their time, in that order, their other Guids read as they were written.
/// </summary>
/// <param name="leg">The provider.</param>
/// <param name="ordered">Whether the database before already stored its Guids by time.</param>
public abstract class StoreKeyRebuildParityTests(ProviderLeg leg, bool ordered) : IAsyncLifetime
{
    private static readonly DateTime Monday = new(2026, 11, 23, 8, 30, 15, 250, DateTimeKind.Utc);

    private static readonly Guid Owner = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (leg.Available)
        {
            await using var db = Before();
            await leg.Recreate(db);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (leg.Available)
        {
            await using var db = Before();
            await leg.Drop(db);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_waiting_row_refuses_the_rebuild_and_the_rows_stay()
    {
        Assert.SkipUnless(leg.Available, leg.SkipReason);
        await SeedAsync(waiting: true);

        await Assert.ThrowsAnyAsync<Exception>(MigrateAsync);

        await using var before = Before();
        Assert.Equal(2, await before.Set<NumberedQueueRow>().CountAsync(Ct));
    }

    [Fact]
    public async Task Processed_rows_come_back_under_time_ordered_ids_with_their_Guids_intact()
    {
        Assert.SkipUnless(leg.Available, leg.SkipReason);
        await SeedAsync(waiting: false);

        await MigrateAsync();

        await using var after = After();
        var rows = await after.Set<GuidQueueRow>().AsNoTracking().OrderBy(r => r.Id).ToListAsync(Ct);
        Assert.Equal(["earlier", "later", "later still"], rows.Select(static r => r.Subject));
        Assert.All(rows, static r => Assert.Equal(7, r.Id.Version));
        Assert.Equal(Owner, rows[0].UserId);
        Assert.Equal(Monday.AddMinutes(-5).ToUnixTimeMilliseconds(), Millisecond(rows[0].Id));
        after.Set<GuidQueueRow>().Add(new GuidQueueRow { Id = Guid.CreateVersion7(), Subject = "new", CreatedAt = Monday });
        await after.SaveChangesAsync(Ct);
    }

    private async Task SeedAsync(bool waiting)
    {
        await using var before = Before();
        var rows = before.Set<NumberedQueueRow>();
        rows.Add(new NumberedQueueRow { Subject = "later", CreatedAt = Monday, ProcessedAt = Monday });
        if (waiting)
        {
            rows.Add(new NumberedQueueRow { Subject = "waiting", CreatedAt = Monday });
        }
        else
        {
            rows.Add(new NumberedQueueRow { Subject = "earlier", CreatedAt = Monday.AddMinutes(-5), ProcessedAt = Monday, UserId = Owner });
            rows.Add(new NumberedQueueRow { Subject = "later still", CreatedAt = Monday, ProcessedAt = Monday });
        }

        await before.SaveChangesAsync(Ct);
    }

    private async Task MigrateAsync()
    {
        await using var before = Before();
        await using var after = After();
        var operations = after.GetService<IMigrationsModelDiffer>().GetDifferences(Model(before), Model(after));
        var commands = after.GetService<IMigrationsSqlGenerator>().Generate(operations, after.GetService<IDesignTimeModel>().Model);
        await after.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(commands, after.GetService<IRelationalConnection>(), Ct);
    }

    private static long Millisecond(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        return (long)(System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes) >> 16);
    }

    private static IRelationalModel Model(DbContext context) => context.GetService<IDesignTimeModel>().Model.GetRelationalModel();

    private NumberedQueueContext Before() => leg.Open<NumberedQueueContext>(o => new NumberedQueueContext(o, ordered));

    private GuidQueueContext After() => leg.Open<GuidQueueContext>(static o => new GuidQueueContext(o));
}

file static class Instants
{
    internal static long ToUnixTimeMilliseconds(this DateTime at) => new DateTimeOffset(at).ToUnixTimeMilliseconds();
}

public sealed class SqliteStoreKeyRebuildParityTests() : StoreKeyRebuildParityTests(ProviderLeg.Sqlite("key-rebuild"), ordered: true);

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerStoreKeyRebuildParityTests() : StoreKeyRebuildParityTests(ProviderLeg.SqlServer("rask_e2e_key_rebuild"), ordered: true);

/// <summary>The rebuild and the first time-ordered rewrite in one migration: the rows come back in the stored form.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerNativeStoreKeyRebuildParityTests() : StoreKeyRebuildParityTests(ProviderLeg.SqlServer("rask_e2e_key_rebuild_native"), ordered: false);

[Collection(PostgresCollection.Name)]
public sealed class PostgresStoreKeyRebuildParityTests() : StoreKeyRebuildParityTests(ProviderLeg.Postgres(NumberedQueueContext.Schema), ordered: true);

/// <summary>The rebuild as SQL Server and PostgreSQL migrations spell it, without a server.</summary>
public sealed class StoreKeyRebuildModelTests
{
    private const string SqlServerOffline = "Server=(local);Database=rask_model_only;Trusted_Connection=True";
    private const string PostgresOffline = "Host=localhost;Database=rask_model_only";

    [Fact]
    public void On_SQL_Server_the_guard_throws_and_the_kept_rows_wait_beside_the_table_in_the_stored_form()
    {
        var sql = Script(o => o.UseRaskSqlServerAt(SqlServerOffline), ordered: true);

        Assert.StartsWith(ExclusiveMigrations.Prefix + ": rebuilds the key of [rask_key_rebuild].[QueueRows]", sql, StringComparison.Ordinal);
        Assert.Contains("THROW 50000", sql, StringComparison.Ordinal);
        Assert.Contains("INTO [rask_key_rebuild].[QueueRows_rask_rebuild]", sql, StringComparison.Ordinal);
        Assert.Contains("DATEDIFF_BIG(millisecond", sql, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE [rask_key_rebuild].[QueueRows]", sql, StringComparison.Ordinal);
        Assert.Contains("[id] uniqueidentifier NOT NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("IDENTITY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER COLUMN", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void On_SQL_Server_a_database_still_native_has_its_other_Guids_rewritten_as_they_come_back()
    {
        var sql = Script(o => o.UseRaskSqlServerAt(SqlServerOffline), ordered: false);

        Assert.Contains(SqlServerGuidOrder.StoredFromNative("rask_rows.[user_id]"), sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE [rask_key_rebuild].[QueueRows]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void On_PostgreSQL_the_guard_raises_and_the_new_id_is_cast_to_uuid()
    {
        var sql = Script(o => o.UseRaskPostgresAt(PostgresOffline), ordered: true);

        // After the model's own collation, which Rask's PostgreSQL provider creates first in every migration.
        Assert.Contains(ExclusiveMigrations.Prefix + ": rebuilds the key of rask_key_rebuild.\"QueueRows\"", sql, StringComparison.Ordinal);
        Assert.Contains("RAISE EXCEPTION", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE rask_key_rebuild.\"QueueRows_rask_rebuild\" AS SELECT", sql, StringComparison.Ordinal);
        Assert.Contains("AS uuid)", sql, StringComparison.Ordinal);
        Assert.Contains("id uuid NOT NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER COLUMN", sql, StringComparison.Ordinal);
    }

    private static string Script(Action<DbContextOptionsBuilder> provider, bool ordered)
    {
        var before = new DbContextOptionsBuilder<NumberedQueueContext>();
        provider(before);
        var after = new DbContextOptionsBuilder<GuidQueueContext>();
        provider(after);
        using var numbered = new NumberedQueueContext(before.Options, ordered);
        using var guids = new GuidQueueContext(after.Options);

        var operations = guids.GetService<IMigrationsModelDiffer>().GetDifferences(
            numbered.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            guids.GetService<IDesignTimeModel>().Model.GetRelationalModel());
        return string.Join("\n", guids.GetService<IMigrationsSqlGenerator>()
            .Generate(operations, guids.GetService<IDesignTimeModel>().Model)
            .Select(static c => c.CommandText));
    }
}
