using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Testing;

namespace Rask.Storage.Tests;

public sealed class StorageDbContext(DbContextOptions<StorageDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskStorage();
}

/// <summary>A hand-rolled fake clock (no external package), so the sweep's grace period is driven deterministically.</summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
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
public sealed class StorageHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly TestFileBackend _uploads = new();

    public StorageHarness(Action<StorageOptions>? configure = null, bool createSchema = true)
    {
        Root = Path.Combine(Path.GetTempPath(), $"rask-storage-test-{Guid.NewGuid():N}");
        DbPath = Root + ".db";
        Clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock); // before AddRaskStorage, so its TryAddSingleton keeps this one
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddRaskStorage<StorageDbContext>(o =>
        {
            o.Disk.Root = Root;
            o.DataVolume = Path.Combine(Root, "no-such-volume");
            configure?.Invoke(o);
        });
        services.AddDbContextFactory<StorageDbContext>(o => o.UseSqlite($"Data Source={DbPath}"));

        _provider = services.BuildServiceProvider();
        if (createSchema)
        {
            using var db = NewContext();
            db.Database.EnsureCreated();
        }
    }

    public string Root { get; }

    public string DbPath { get; }

    public FakeTimeProvider Clock { get; }

    public IServiceProvider Services => _provider;

    public IFiles Files => _provider.GetRequiredService<IFiles>();

    internal StorageRuntime Runtime => _provider.GetRequiredService<StorageRuntime>();

    internal OrphanSweeper<StorageDbContext> Sweeper =>
        _provider.GetServices<IHostedService>().OfType<OrphanSweeper<StorageDbContext>>().Single();

    public TestFile Upload(string name, byte[] bytes, string? browserType = null) => _uploads.Add(name, bytes, browserType);

    public StorageDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<StorageDbContext>>().CreateDbContext();

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
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        TryDelete(() => File.Delete(DbPath));
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
