using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Core.Live;

namespace Rask.Storage.Tests;

[Collection(StorageDbCollection.Name)]
public sealed class FilesTests
{
    [Fact]
    public async Task Saving_an_upload_records_a_row_and_stores_the_bytes()
    {
        await using var harness = new StorageHarness();
        var bytes = Samples.Png(4096);

        var file = await harness.Files.SaveAsync(harness.Upload("../avatar.png", bytes, "text/html"));

        Assert.Equal("avatar.png", file.Name);
        Assert.Equal("image/png", file.ContentType); // sniffed; the browser said text/html
        Assert.Equal(bytes.Length, file.Size);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), file.Sha256);
        Assert.Equal(StorageProvider.Disk, file.Provider);
        Assert.Equal(KeyLayout.KeyOf("", file.Id), file.Key);
        Assert.False(file.Public);

        var stored = await harness.Files.FindAsync(file.Id);
        Assert.Equal(file, stored);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(harness.Root, file.Key)));
        Assert.Empty(harness.SpoolPaths());
    }

    [Fact]
    public async Task A_stream_saves_under_the_name_given()
    {
        await using var harness = new StorageHarness();
        using var content = new MemoryStream(Samples.Text);

        var file = await harness.Files.SaveAsync(content, "data.csv", o => o.Public = true);

        Assert.Equal("text/csv", file.ContentType);
        Assert.True(file.Public);
        Assert.Equal(Samples.Text.Length, file.Size);
    }

    [Fact]
    public async Task An_empty_file_is_a_file()
    {
        await using var harness = new StorageHarness();
        var file = await harness.Files.SaveAsync(new MemoryStream(), "empty.bin");

        Assert.Equal(0, file.Size);
        Assert.Equal("application/octet-stream", file.ContentType);
    }

    [Fact]
    public async Task A_declared_size_over_the_limit_is_refused_before_reading()
    {
        await using var harness = new StorageHarness(o => o.MaxFileSize = 100);

        var ex = await Assert.ThrowsAsync<FileRejectedException>(() =>
            harness.Files.SaveAsync(harness.Upload("big.png", Samples.Png(101))));

        Assert.Equal(FileRejection.TooLarge, ex.Reason);
        Assert.Equal(100, ex.Limit);
        Assert.DoesNotContain("big.png", ex.Message);
        Assert.Equal(0, await harness.CountRowsAsync());
        Assert.Empty(harness.StoredPaths());
    }

    [Fact]
    public async Task A_stream_that_runs_past_the_limit_is_refused_and_leaves_nothing()
    {
        await using var harness = new StorageHarness(o => o.MaxFileSize = 100_000);

        var ex = await Assert.ThrowsAsync<FileRejectedException>(() =>
            harness.Files.SaveAsync(new MemoryStream(Samples.Png(250_000)), "big.png"));

        Assert.Equal(FileRejection.TooLarge, ex.Reason);
        Assert.Equal(0, await harness.CountRowsAsync());
        Assert.Empty(harness.StoredPaths());
        Assert.Empty(harness.SpoolPaths());
    }

    [Fact]
    public async Task The_storage_limit_is_passed_to_the_upload_not_its_512KB_default()
    {
        await using var harness = new StorageHarness();
        var file = await harness.Files.SaveAsync(harness.Upload("large.png", Samples.Png(600_000)));

        Assert.Equal(600_000, file.Size);
    }

    [Fact]
    public async Task A_type_outside_AllowedTypes_is_refused_by_what_the_bytes_are()
    {
        await using var harness = new StorageHarness(o => o.AllowedTypes.Add("image/*"));

        var ex = await Assert.ThrowsAsync<FileRejectedException>(() =>
            harness.Files.SaveAsync(harness.Upload("photo.png", Samples.Html, "image/png")));

        Assert.Equal(FileRejection.TypeNotAllowed, ex.Reason);
        Assert.Equal("text/html", ex.ContentType);
        Assert.Contains("o.AllowedTypes.Add(\"text/html\")", ex.Message);
        Assert.Equal(0, await harness.CountRowsAsync());
        Assert.Empty(harness.StoredPaths());

        Assert.Equal("image/png", (await harness.Files.SaveAsync(harness.Upload("ok.png", Samples.Png()))).ContentType);
    }

    [Fact]
    public async Task A_file_reads_back_and_a_missing_one_is_null()
    {
        await using var harness = new StorageHarness();
        var bytes = Samples.Png(1000);
        var file = await harness.Files.SaveAsync(harness.Upload("a.png", bytes));

        await using (var stream = await harness.Files.OpenReadAsync(file.Id))
        {
            var copy = new MemoryStream();
            await stream!.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }

        Assert.Null(await harness.Files.OpenReadAsync(Guid.NewGuid()));
        Assert.Null(await harness.Files.FindAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Delete_removes_the_row_then_the_bytes()
    {
        await using var harness = new StorageHarness();
        var file = await harness.Files.SaveAsync(harness.Upload("a.png", Samples.Png()));

        Assert.True(await harness.Files.DeleteAsync(file.Id));

        Assert.Null(await harness.Files.FindAsync(file.Id));
        Assert.Empty(harness.StoredPaths());
        Assert.False(await harness.Files.DeleteAsync(file.Id));
    }

    [Fact]
    public async Task A_public_url_is_built_from_the_id_alone()
    {
        await using var harness = new StorageHarness();
        var id = Guid.Parse("3f2a0000-0000-0000-0000-000000000001");

        Assert.Equal("/_rask/files/public/3f2a0000000000000000000000000001", harness.Files.Url(id));
    }

    [Fact]
    public async Task A_public_base_url_joins_the_key()
    {
        await using var harness = new StorageHarness(o =>
        {
            o.PublicBaseUrl = "https://cdn.example.com/files";
            o.Prefix = "myapp";
        });
        var id = Guid.Parse("3f2a0000-0000-0000-0000-000000000001");

        Assert.Equal("https://cdn.example.com/files/myapp/3f/3f2a0000000000000000000000000001", harness.Files.Url(id));
    }

    [Fact]
    public async Task A_temporary_url_on_disk_is_a_signed_app_route_that_opens_to_the_file()
    {
        await using var harness = new StorageHarness();
        var file = await harness.Files.SaveAsync(harness.Upload("a.pdf", "%PDF-1.7\n"u8.ToArray()));

        var url = await harness.Files.TemporaryUrlAsync(file.Id, TimeSpan.FromMinutes(5));

        Assert.NotNull(url);
        Assert.StartsWith("/_rask/files/", url);
        Assert.True(harness.Runtime.Protector.TryUnprotect(url!["/_rask/files/".Length..], out var id));
        Assert.Equal(file.Id, id);
        Assert.Null(await harness.Files.TemporaryUrlAsync(Guid.NewGuid(), TimeSpan.FromMinutes(5)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    [InlineData(7 * 24 * 60 * 60 + 1)]
    public async Task A_temporary_url_lasts_more_than_zero_and_at_most_seven_days(int seconds)
    {
        await using var harness = new StorageHarness();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            harness.Files.TemporaryUrlAsync(Guid.NewGuid(), TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public async Task Urls_honour_the_path_base()
    {
        await using var harness = new StorageHarness();
        try
        {
            LiveOptions.PathBase = "/appA";
            Assert.StartsWith("/appA/_rask/files/public/", harness.Files.Url(Guid.NewGuid()));
        }
        finally
        {
            LiveOptions.PathBase = string.Empty;
        }
    }
}

[Collection(StorageDbCollection.Name)]
public sealed class StorageRegistrationTests
{
    [Fact]
    public async Task Registering_twice_adds_one_sweeper_and_one_of_each_check()
    {
        await using var harness = new StorageHarness();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskStorage<StorageDbContext>();
        services.AddRaskStorage<StorageDbContext>();

        var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType).ToList();
        Assert.Single(hosted, t => t == typeof(OrphanSweeper<StorageDbContext>));
        Assert.Single(hosted, t => t == typeof(StorageStartupCheck));
        Assert.Single(hosted, t => t == typeof(StorageModelCheck<StorageDbContext>));
    }

    [Fact]
    public async Task An_unmapped_model_fails_the_boot_with_the_line_to_add()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"rask-storage-unmapped-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskStorage<UnmappedDbContext>();
        services.AddDbContextFactory<UnmappedDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        await using var provider = services.BuildServiceProvider();

        var check = provider.GetServices<IHostedService>().OfType<StorageModelCheck<UnmappedDbContext>>().Single();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => check.StartAsync(default));

        Assert.Contains("modelBuilder.AddRaskStorage();", ex.Message);
    }

    [Fact]
    public async Task Bad_configuration_fails_the_boot_not_the_first_upload()
    {
        await using var harness = new StorageHarness(o => o.MaxFileSize = -5);

        // What a host does at startup: resolve every hosted service, then start each. The sweep takes the
        // runtime, so resolving it already builds and validates the options.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            foreach (var service in harness.Services.GetServices<IHostedService>())
            {
                await service.StartAsync(default);
            }
        });

        Assert.Contains("MaxFileSize", ex.Message);
    }

    public sealed class UnmappedDbContext(DbContextOptions<UnmappedDbContext> options) : DbContext(options);
}
