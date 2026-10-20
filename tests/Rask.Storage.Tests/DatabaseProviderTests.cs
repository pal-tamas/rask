using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Storage.Tests;

/// <summary>
/// <c>Rask__Storage__Provider=Database</c>: the bytes in a <c>StoredFileBytes</c> row beside the file's own, served,
/// deleted and swept like any other store's — here on a real SQLite file.
/// </summary>
[Collection(StorageDbCollection.Name)]
public sealed class DatabaseProviderTests
{
    private static readonly Action<StorageOptions> InTheDatabase = o => o.Provider = StorageProvider.Database;

    [Fact]
    public async Task A_file_saved_to_the_database_reads_back_whole_and_leaves_nothing_on_disk()
    {
        await using var harness = new StorageHarness(InTheDatabase);
        var bytes = Samples.Png(300_000);

        var file = await harness.Files.Save(new MemoryStream(bytes), "scan.png", TestContext.Current.CancellationToken);
        await using var read = await harness.Files.OpenRead(file.Id, TestContext.Current.CancellationToken);

        Assert.Equal(StorageProvider.Database, file.Provider);
        Assert.Equal(bytes, await ReadAllAsync(read!));
        Assert.Empty(harness.StoredPaths());
        Assert.Equal([file.Id], await BytesIdsAsync(harness));
    }

    [Fact]
    public async Task Deleting_a_file_removes_its_bytes_row()
    {
        await using var harness = new StorageHarness(InTheDatabase);
        var file = await harness.Files.Save(new MemoryStream(Samples.Png()), "a.png", TestContext.Current.CancellationToken);

        var deleted = await harness.Files.Delete(file.Id, TestContext.Current.CancellationToken);

        Assert.True(deleted);
        Assert.Empty(await BytesIdsAsync(harness));
        Assert.Equal(0, await harness.CountRowsAsync());
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_and_writes_no_bytes()
    {
        await using var harness = new StorageHarness(o =>
        {
            o.Provider = StorageProvider.Database;
            o.MaxFileSize = 1000;
        });

        await Assert.ThrowsAsync<FileRejectedException>(
            () => harness.Files.Save(new MemoryStream(Samples.Png(5000)), "big.png", TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(await BytesIdsAsync(harness));
        Assert.Equal(0, await harness.CountRowsAsync());
    }

    [Fact]
    public async Task The_sweep_removes_bytes_no_file_claims_and_keeps_the_rest()
    {
        await using var harness = new StorageHarness(InTheDatabase);
        var kept = await harness.Files.Save(new MemoryStream(Samples.Png()), "kept.png", TestContext.Current.CancellationToken);
        var orphan = Guid.NewGuid();
        await using (var db = harness.NewContext())
        {
            db.Set<StoredFileBytes>().Add(new StoredFileBytes { Id = orphan, Bytes = Samples.Png() });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await harness.Sweeper.SweepAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Deleted);
        Assert.Equal([kept.Id], await BytesIdsAsync(harness));
    }

    [Fact]
    public async Task Download_url_and_temporary_url_serve_the_bytes_with_ranges_and_the_etag()
    {
        await using var host = await FileHost.StartAsync(configure: InTheDatabase);
        var bytes = Samples.Png(200_000);
        var file = await host.Files.Save(new MemoryStream(bytes), "a.png", TestContext.Current.CancellationToken).Public();
        var shared = await host.Files.Share(file.Id, TestContext.Current.CancellationToken).For(TimeSpan.FromMinutes(5));

        using var download = await host.Client.GetAsync($"/download/{file.Id}", TestContext.Current.CancellationToken);
        using var temporary = await host.Client.GetAsync(shared, TestContext.Current.CancellationToken);
        using var range = new HttpRequestMessage(HttpMethod.Get, host.Files.Url(file.Id));
        range.Headers.Range = new RangeHeaderValue(150_000, 150_099);
        using var partial = await host.Client.SendAsync(range, TestContext.Current.CancellationToken);
        using var conditional = new HttpRequestMessage(HttpMethod.Get, host.Files.Url(file.Id));
        conditional.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"" + file.Sha256 + "\""));
        using var notModified = await host.Client.SendAsync(conditional, TestContext.Current.CancellationToken);

        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("\"" + file.Sha256 + "\"", download.Headers.ETag?.Tag);
        Assert.Equal(bytes, await temporary.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal(bytes[150_000..150_100], await partial.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
    }

    [Fact]
    public async Task A_database_provider_without_the_bytes_table_stops_the_boot()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskStorage<StoredFileOnlyContext>(InTheDatabase);
        services.AddDbContextFactory<StoredFileOnlyContext>(o => o.UseSqlite("Data Source=:memory:"));
        await using var provider = services.BuildServiceProvider();
        var check = provider.GetServices<IHostedService>().OfType<StorageBytesModelCheck<StoredFileOnlyContext>>().Single();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => check.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("StoredFileBytes", refused.Message, StringComparison.Ordinal);
        Assert.Contains("modelBuilder.AddRaskStorage();", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_provider_does_not_need_the_bytes_table()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskStorage<StoredFileOnlyContext>(o => o.Disk.Root = Path.GetTempPath());
        services.AddDbContextFactory<StoredFileOnlyContext>(o => o.UseSqlite("Data Source=:memory:"));
        await using var provider = services.BuildServiceProvider();
        var check = provider.GetServices<IHostedService>().OfType<StorageBytesModelCheck<StoredFileOnlyContext>>().Single();

        await check.StartAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(check);
    }

    [Fact]
    public void A_public_base_url_cannot_serve_files_kept_in_the_database()
    {
        var options = new StorageOptions { Provider = StorageProvider.Database, PublicBaseUrl = "https://files.example.com/" };

        var refused = Assert.Throws<InvalidOperationException>(() => options.Validate(webRootPath: null));

        Assert.Contains("PublicBaseUrl", refused.Message, StringComparison.Ordinal);
    }

    private static async Task<Guid[]> BytesIdsAsync(StorageHarness harness)
    {
        await using var db = harness.NewContext();
        return await db.Set<StoredFileBytes>().Select(b => b.Id).ToArrayAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, TestContext.Current.CancellationToken);
        return copy.ToArray();
    }
}

/// <summary>A context that maps the file rows by hand and forgot the bytes table.</summary>
public sealed class StoredFileOnlyContext(DbContextOptions<StoredFileOnlyContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new StoredFileConfiguration());
}
