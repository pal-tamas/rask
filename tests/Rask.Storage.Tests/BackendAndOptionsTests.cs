using System.Buffers.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Rask.Storage.Backends;
using Rask.Storage.Serving;

namespace Rask.Storage.Tests;

public sealed class DiskBlobBackendTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rask-disk-backend-{Guid.NewGuid():N}");

    private static BlobHeaders Headers => new("image/png", "inline", "private, no-store");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task A_spooled_file_moves_into_place_and_reads_back()
    {
        var backend = new DiskBlobBackend(_root);
        var spool = await SpoolAsync(backend, [1, 2, 3, 4, 5]);

        await backend.PutFileAsync("ab/abcd", spool, 5, Headers, default);

        Assert.False(File.Exists(spool));
        await using var stream = await backend.OpenReadAsync("ab/abcd", 2, null, default);
        Assert.NotNull(stream);
        var rest = new MemoryStream();
        await stream!.CopyToAsync(rest);
        Assert.Equal([3, 4, 5], rest.ToArray());
    }

    [Fact]
    public async Task Storing_never_overwrites()
    {
        var backend = new DiskBlobBackend(_root);
        await backend.PutFileAsync("ab/one", await SpoolAsync(backend, [1]), 1, Headers, default);

        var second = await SpoolAsync(backend, [2]);
        Assert.ThrowsAny<IOException>(() => backend.PutFileAsync("ab/one", second, 1, Headers, default).GetAwaiter().GetResult());
    }

    [Fact]
    public void Nothing_is_created_until_the_first_write()
    {
        _ = new DiskBlobBackend(_root);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Missing_objects_read_as_null_and_delete_quietly()
    {
        var backend = new DiskBlobBackend(_root);

        Assert.Null(await backend.OpenReadAsync("ab/none", 0, null, default));
        await backend.DeleteAsync("ab/none", default);
    }

    [Fact]
    public async Task The_listing_skips_the_spool_and_honours_the_prefix()
    {
        var backend = new DiskBlobBackend(_root);
        await backend.PutFileAsync("app/ab/one", await SpoolAsync(backend, [1]), 1, Headers, default);
        await backend.PutFileAsync("other/ab/two", await SpoolAsync(backend, [2]), 1, Headers, default);
        _ = await SpoolAsync(backend, [3]); // a save in flight

        var keys = await backend.ListAsync("app/", default).Select(e => e.Key).ToListAsync();

        Assert.Equal(["app/ab/one"], keys);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("ab/../../escape")]
    [InlineData("/etc/passwd")]
    [InlineData("AB/upper")]
    public async Task A_key_that_is_not_a_plain_key_is_refused(string key)
    {
        var backend = new DiskBlobBackend(_root);

        await Assert.ThrowsAsync<ArgumentException>(() => backend.OpenReadAsync(key, 0, null, default));
        await Assert.ThrowsAsync<ArgumentException>(() => backend.DeleteAsync(key, default));
    }

    [Fact]
    public async Task Stale_spool_files_are_removed_and_fresh_ones_kept()
    {
        var backend = new DiskBlobBackend(_root);
        var stale = await SpoolAsync(backend, [1]);
        var fresh = await SpoolAsync(backend, [2]);
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));

        await backend.DeleteStaleSpoolAsync(DateTimeOffset.UtcNow.AddDays(-1), default);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void A_disk_store_never_presigns() =>
        Assert.False(new DiskBlobBackend(_root).TryPresign("ab/x", TimeSpan.FromMinutes(1), "image/png", "inline", out _));

    private static async Task<string> SpoolAsync(DiskBlobBackend backend, byte[] content)
    {
        var path = backend.CreateSpoolPath();
        await File.WriteAllBytesAsync(path, content);
        return path;
    }
}

public sealed class TemporaryUrlProtectorTests
{
    private readonly EphemeralDataProtectionProvider _provider = new();

    [Fact]
    public void A_token_round_trips_its_id()
    {
        var protector = new TemporaryUrlProtector(_provider);
        var id = Guid.NewGuid();

        var token = protector.Protect(id, TimeSpan.FromMinutes(5));

        Assert.True(token.Length <= TemporaryUrlProtector.MaxTokenChars);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('+', token);
        Assert.True(protector.TryUnprotect(token, out var opened));
        Assert.Equal(id, opened);
    }

    [Fact]
    public void An_expired_token_does_not_open()
    {
        var protector = new TemporaryUrlProtector(_provider);
        var payload = new byte[17];
        payload[0] = 1;
        Guid.NewGuid().TryWriteBytes(payload.AsSpan(1));
        var expired = _provider.CreateProtector(TemporaryUrlProtector.Purpose).ToTimeLimitedDataProtector()
            .Protect(payload, DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.False(protector.TryUnprotect(Base64Url.EncodeToString(expired), out _));
    }

    [Fact]
    public void A_token_sealed_for_another_purpose_does_not_open()
    {
        var other = _provider.CreateProtector("Rask.LiveSession.Resume.v1").ToTimeLimitedDataProtector()
            .Protect(new byte[17], TimeSpan.FromMinutes(5));

        Assert.False(new TemporaryUrlProtector(_provider).TryUnprotect(Base64Url.EncodeToString(other), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64url!!")]
    [InlineData("AAAA")]
    public void Garbage_never_throws(string? token) =>
        Assert.False(new TemporaryUrlProtector(_provider).TryUnprotect(token, out _));

    [Fact]
    public void A_tampered_or_oversized_token_does_not_open()
    {
        var protector = new TemporaryUrlProtector(_provider);
        var token = protector.Protect(Guid.NewGuid(), TimeSpan.FromMinutes(5));
        var tampered = (token[10] == 'A' ? 'B' : 'A') + token[..10] + token[11..];

        Assert.False(protector.TryUnprotect(tampered, out _));
        Assert.False(protector.TryUnprotect(new string('A', TemporaryUrlProtector.MaxTokenChars + 1), out _));
    }
}

public sealed class StorageOptionsTests
{
    [Fact]
    public void Configuration_keys_land_on_the_options()
    {
        var options = new StorageOptions();
        StorageConfiguration.Apply(options, Config(new()
        {
            ["Storage:Provider"] = "disk",
            ["Storage:MaxFileSize"] = "1048576",
            ["Storage:PublicBaseUrl"] = "https://files.example.com",
            ["Storage:Prefix"] = "myapp",
            ["Storage:Disk:Root"] = "uploads",
        }));

        Assert.Equal(StorageProvider.Disk, options.Provider);
        Assert.Equal(1048576, options.MaxFileSize);
        Assert.Equal("https://files.example.com", options.PublicBaseUrl);
        Assert.Equal("myapp", options.Prefix);
        Assert.Equal("uploads", options.Disk.Root);
    }

    [Theory]
    [InlineData("Nope")]
    [InlineData("7")]
    public void An_unknown_provider_names_the_key_and_the_choices(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StorageConfiguration.Apply(new StorageOptions(), Config(new() { ["Storage:Provider"] = value })));

        Assert.Contains("Storage__Provider", ex.Message);
        Assert.Contains("Disk", ex.Message);
    }

    [Fact]
    public void A_non_numeric_size_names_the_key() =>
        Assert.Contains("Storage__MaxFileSize", Assert.Throws<InvalidOperationException>(() =>
            StorageConfiguration.Apply(new StorageOptions(), Config(new() { ["Storage:MaxFileSize"] = "50MB" }))).Message);

    [Fact]
    public void The_disk_root_prefers_config_then_the_volume_then_the_content_root()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"rask-root-{Guid.NewGuid():N}");
        var volume = Path.Combine(temp, "volume");
        try
        {
            var relative = new StorageOptions { DataVolume = volume };
            relative.Disk.Root = "uploads";
            StorageConfiguration.ResolveDiskRoot(relative, temp);
            Assert.Equal(Path.Combine(temp, "uploads"), relative.Disk.Root);

            var noVolume = new StorageOptions { DataVolume = volume };
            StorageConfiguration.ResolveDiskRoot(noVolume, temp);
            Assert.Equal(Path.Combine(temp, "storage"), noVolume.Disk.Root);

            Directory.CreateDirectory(volume);
            var withVolume = new StorageOptions { DataVolume = volume };
            StorageConfiguration.ResolveDiskRoot(withVolume, temp);
            Assert.Equal(Path.Combine(volume, "files"), withVolume.Disk.Root);

            // Resolution creates nothing: an app that never saves a file never grows a directory.
            Assert.False(Directory.Exists(Path.Combine(temp, "storage")));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void A_root_inside_wwwroot_is_refused()
    {
        var web = Path.Combine(Path.GetTempPath(), "app", "wwwroot");
        var options = Resolved(Path.Combine(web, "uploads"));

        Assert.Contains("Storage__Disk__Root", Assert.Throws<InvalidOperationException>(() => options.Validate(web)).Message);
    }

    [Fact]
    public void A_sibling_of_wwwroot_that_shares_its_name_prefix_is_fine() =>
        Resolved(Path.Combine(Path.GetTempPath(), "app", "wwwroot-files")).Validate(Path.Combine(Path.GetTempPath(), "app", "wwwroot"));

    [Theory]
    [InlineData(".pdf")]
    [InlineData("pdf")]
    [InlineData("image/png; charset=x")]
    [InlineData("image/")]
    public void An_allowed_type_that_is_not_a_media_type_is_refused(string type)
    {
        var options = Resolved("/tmp/rask-files");
        options.AllowedTypes.Add(type);

        Assert.Contains("application/pdf", Assert.Throws<InvalidOperationException>(() => options.Validate(null)).Message);
    }

    [Theory]
    [InlineData("http://files.example.com")]
    [InlineData("https://files.example.com/?sig=1")]
    [InlineData("files.example.com")]
    public void A_public_base_url_must_be_absolute_https(string url)
    {
        var options = Resolved("/tmp/rask-files");
        options.PublicBaseUrl = url;

        Assert.Throws<InvalidOperationException>(() => options.Validate(null));
    }

    [Fact]
    public void Prefixes_and_base_urls_are_normalized()
    {
        var options = Resolved("/tmp/rask-files");
        options.Prefix = "myapp";
        options.PublicBaseUrl = "https://cdn.example.com/files";

        options.Validate(null);

        Assert.Equal("myapp/", options.Prefix);
        Assert.Equal("https://cdn.example.com/files/", options.PublicBaseUrl);
    }

    [Fact]
    public void Localhost_may_use_http()
    {
        var options = Resolved("/tmp/rask-files");
        options.PublicBaseUrl = "http://localhost:9000/bucket";
        options.Validate(null);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void A_size_limit_must_be_positive(long size)
    {
        var options = Resolved("/tmp/rask-files");
        options.MaxFileSize = size;
        Assert.Throws<InvalidOperationException>(() => options.Validate(null));
    }

    [Fact]
    public void The_grace_period_has_a_floor()
    {
        var options = Resolved("/tmp/rask-files");
        options.OrphanGracePeriod = TimeSpan.FromMinutes(1);
        Assert.Throws<InvalidOperationException>(() => options.Validate(null));
    }

    [Fact]
    public void A_bad_prefix_is_refused()
    {
        var options = Resolved("/tmp/rask-files");
        options.Prefix = "../up";
        Assert.Throws<InvalidOperationException>(() => options.Validate(null));
    }

    private static StorageOptions Resolved(string root)
    {
        var options = new StorageOptions();
        options.Disk.Root = root;
        return options;
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
