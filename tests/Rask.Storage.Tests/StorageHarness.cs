using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Testing;
using Rask.Tests.Shared;

namespace Rask.Storage.Tests;

public sealed class StorageDbContext(DbContextOptions<StorageDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskStorage();
}

/// <summary>Byte shapes the sniffer recognises.</summary>
internal static class Samples
{
    public static byte[] Png(int size = 256)
    {
        var bytes = new byte[size];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes, 0);
        for (var i = signature.Length; i < size; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }

    public static byte[] Html => "<!DOCTYPE html><html><body><script>alert(1)</script></body></html>"u8.ToArray();

    public static byte[] Text => "plain words, nothing else"u8.ToArray();
}

/// <summary>A real-SQLite, real-directory service provider wired for storage, with a controllable clock.</summary>
public sealed class StorageHarness(
    Action<StorageOptions>? configure = null, bool createSchema = true, string? connectionString = null)
    : StorageHarness<StorageDbContext>(configure, createSchema, connectionString);

/// <summary>The same over any context, for a model that differs — one whose tenants are numbered.</summary>
public class StorageHarness<TContext> : IAsyncDisposable
    where TContext : DbContext
{
    private readonly ServiceProvider _provider;
    private readonly TestFileBackend _uploads = new();

    // The database this harness made, and so holds open and closes; null when the test handed it one of its own.
    private readonly InMemorySqlite? _ownDb;

    /// <param name="configure">The storage options the test sets.</param>
    /// <param name="createSchema">Whether to create the context's tables.</param>
    /// <param name="connectionString">
    ///     A database the test keeps itself, for one whose test is about what a file gives; null gives the harness a
    ///     database of its own, in memory.
    /// </param>
    public StorageHarness(Action<StorageOptions>? configure = null, bool createSchema = true, string? connectionString = null)
    {
        if (connectionString is null)
        {
            _ownDb = new InMemorySqlite();
            connectionString = _ownDb.ConnectionString;
        }

        ConnectionString = connectionString;
        Root = Path.Combine(Path.GetTempPath(), $"rask-storage-test-{Guid.NewGuid():N}");
        Clock = new ManualClock(DateTimeOffset.UtcNow);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock); // before AddRaskStorage, so its TryAddSingleton keeps this one
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddRaskStorage<TContext>(o =>
        {
            o.Disk.Root = Root;
            o.DataVolume = Path.Combine(Root, "no-such-volume");
            configure?.Invoke(o);
        });
        services.AddDbContextFactory<TContext>(o => o.UseSqlite(ConnectionString));

        _provider = services.BuildServiceProvider();
        if (createSchema)
        {
            using var db = NewContext();
            db.Database.EnsureCreated();
        }
    }

    public string Root { get; }

    /// <summary>The connection string of the harness's database: what another context or a raw connection opens.</summary>
    public string ConnectionString { get; }

    public ManualClock Clock { get; }

    public IServiceProvider Services => _provider;

    public IFiles Files => _provider.GetRequiredService<IFiles>();

    internal StorageRuntime Runtime => _provider.GetRequiredService<StorageRuntime>();

    internal OrphanSweeper<TContext> Sweeper =>
        _provider.GetServices<IHostedService>().OfType<OrphanSweeper<TContext>>().Single();

    public TestFile Upload(string name, byte[] bytes, string? browserType = null) => _uploads.Add(name, bytes, browserType);

    /// <summary>Saves a picked file the way a component does: its opener, its name, and its declared size.</summary>
    public Task<StoredFile> SaveUploadAsync(TestFile upload, bool isPublic = false)
    {
        var saving = Files.Save(upload.OpenReadStream, upload.Name, upload.Size);
        return (isPublic ? saving.Public() : saving).AsTask();
    }

    public TContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext();

    public async Task<int> CountRowsAsync()
    {
        await using var db = NewContext();
        return await db.Set<StoredFile>().CountAsync();
    }

    /// <summary>Every stored object's path, excluding the spool.</summary>
    public string[] StoredPaths() =>
        Directory.Exists(Root)
            ? Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + ".tmp" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .ToArray()
            : [];

    public string[] SpoolPaths()
    {
        var spool = Path.Combine(Root, ".tmp");
        return Directory.Exists(spool) ? Directory.GetFiles(spool) : [];
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await _provider.DisposeAsync();
        if (_ownDb is not null)
        {
            await _ownDb.DisposeAsync();
        }

        TryDelete(() => Directory.Delete(Root, recursive: true));
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a green run over.
        }
    }
}
