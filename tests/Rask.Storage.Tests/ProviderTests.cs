using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.Storage.Backends;

namespace Rask.Storage.Tests;

public sealed class BlobRangeStreamTests
{
    private static readonly byte[] Content = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();

    [Fact]
    public async Task Nothing_is_fetched_until_the_first_read()
    {
        var store = new MemoryBackend(Content);
        await using var stream = new BlobRangeStream(store, "ab/k", Content.Length, null, null);

        Assert.True(stream.CanSeek);
        Assert.Equal(100, stream.Length);
        stream.Seek(40, SeekOrigin.Begin);
        Assert.Empty(store.Opens);
    }

    [Fact]
    public async Task A_read_after_a_seek_opens_at_that_position()
    {
        var store = new MemoryBackend(Content);
        await using var stream = new BlobRangeStream(store, "ab/k", Content.Length, null, null);

        stream.Seek(90, SeekOrigin.Begin);
        var rest = new MemoryStream();
        await stream.CopyToAsync(rest);

        Assert.Equal(Content[90..], rest.ToArray());
        Assert.Equal([(90L, (long?)null)], store.Opens);
    }

    [Fact]
    public async Task The_requested_range_is_fetched_alone_and_reading_on_reopens_to_the_end()
    {
        var store = new MemoryBackend(Content);
        await using var stream = new BlobRangeStream(store, "ab/k", Content.Length, 10, 19);

        stream.Seek(10, SeekOrigin.Begin);
        var buffer = new byte[10];
        await stream.ReadExactlyAsync(buffer);
        Assert.Equal(Content[10..20], buffer);
        Assert.Equal([(10L, (long?)10)], store.Opens);

        var next = new byte[5];
        await stream.ReadExactlyAsync(next);
        Assert.Equal(Content[20..25], next);
        Assert.Equal((20L, (long?)null), store.Opens[^1]);
    }

    [Fact]
    public async Task Missing_bytes_fail_the_read_rather_than_end_it()
    {
        await using var stream = new BlobRangeStream(new MemoryBackend(null), "ab/k", 10, null, null);

        await Assert.ThrowsAsync<IOException>(async () => await stream.ReadExactlyAsync(new byte[4]));
    }

    private sealed class MemoryBackend(byte[]? content) : IBlobBackend
    {
        public List<(long Offset, long? Count)> Opens { get; } = [];

        public StorageProvider Provider => StorageProvider.S3;

        public long MaxSinglePutBytes => long.MaxValue;

        public string CreateSpoolPath() => throw new NotSupportedException();

        public Task PutFileAsync(string key, string sourcePath, long length, BlobHeaders headers, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string key, long offset, long? count, CancellationToken cancellationToken)
        {
            Opens.Add((offset, count));
            if (content is null)
            {
                return Task.FromResult<Stream?>(null);
            }

            var end = count is { } c ? Math.Min(content.Length, offset + c) : content.Length;
            return Task.FromResult<Stream?>(new MemoryStream(content[(int)offset..(int)end]));
        }

        public Task<Stream?> OpenForServingAsync(string key, long size, long? rangeFrom, long? rangeTo, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerable<BlobEntry> ListAsync(string prefix, CancellationToken cancellationToken) => throw new NotSupportedException();

        public bool TryPresign(string key, TimeSpan lifetime, string contentType, string contentDisposition, [NotNullWhen(true)] out string? url) =>
            throw new NotSupportedException();

        public Task DeleteStaleSpoolAsync(DateTimeOffset olderThan, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

public sealed class ProviderOptionsTests
{
    [Fact]
    public void S3_and_azure_keys_land_on_the_options()
    {
        var options = new StorageOptions();
        StorageConfiguration.Apply(options, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "s3",
            ["Storage:S3:ServiceUrl"] = "https://s3.us-east-1.amazonaws.com",
            ["Storage:S3:Bucket"] = "uploads",
            ["Storage:S3:Region"] = "eu-west-2",
            ["Storage:S3:AccessKeyId"] = "AKID",
            ["Storage:S3:SecretAccessKey"] = "SECRET",
            ["Storage:S3:UsePathStyle"] = "false",
            ["Storage:Azure:ConnectionString"] = "UseDevelopmentStorage=true",
            ["Storage:Azure:Container"] = "files",
        }).Build());

        Assert.Equal(StorageProvider.S3, options.Provider);
        Assert.Equal(new Uri("https://s3.us-east-1.amazonaws.com"), options.S3.ServiceUrl);
        Assert.Equal("uploads", options.S3.Bucket);
        Assert.Equal("eu-west-2", options.S3.Region);
        Assert.Equal("AKID", options.S3.AccessKeyId);
        Assert.Equal("SECRET", options.S3.SecretAccessKey);
        Assert.False(options.S3.UsePathStyle);
        Assert.Equal("UseDevelopmentStorage=true", options.Azure.ConnectionString);
        Assert.Equal("files", options.Azure.Container);
    }

    [Theory]
    [InlineData(null, "uploads", "AKID", "SECRET", "Storage__S3__ServiceUrl")]
    [InlineData("https://s3.example.com", "Up_Loads", "AKID", "SECRET", "Storage__S3__Bucket")]
    [InlineData("https://s3.example.com", "uploads", null, "SECRET", "Storage__S3__AccessKeyId")]
    [InlineData("https://s3.example.com", "uploads", "AKID", null, "Storage__S3__SecretAccessKey")]
    public void An_incomplete_s3_configuration_names_the_missing_key(string? url, string bucket, string? key, string? secret, string named)
    {
        var options = S3(url, bucket, key, secret);

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate(null));

        Assert.Contains(named, ex.Message);
        Assert.DoesNotContain("SECRET", ex.Message);
    }

    [Fact]
    public void A_dotted_bucket_is_refused_under_virtual_host_https()
    {
        var options = S3("https://s3.example.com", "my.bucket", "AKID", "SECRET");
        options.S3.UsePathStyle = false;

        Assert.Contains("UsePathStyle", Assert.Throws<InvalidOperationException>(() => options.Validate(null)).Message);
    }

    [Fact]
    public void A_size_limit_above_one_put_is_refused()
    {
        var options = S3("https://s3.example.com", "uploads", "AKID", "SECRET");
        options.MaxFileSize = 6L * 1024 * 1024 * 1024;

        Assert.Contains("multipart", Assert.Throws<InvalidOperationException>(() => options.Validate(null)).Message);
    }

    [Theory]
    [InlineData(null, "files")]
    [InlineData("UseDevelopmentStorage=true", "Files")]
    [InlineData("UseDevelopmentStorage=true", "a--b")]
    public void An_incomplete_azure_configuration_is_refused(string? connectionString, string container)
    {
        var options = new StorageOptions { Provider = StorageProvider.Azure };
        options.Azure.ConnectionString = connectionString;
        options.Azure.Container = container;

        Assert.Throws<InvalidOperationException>(() => options.Validate(null));
    }

    [Fact]
    public async Task Configuration_selects_the_store()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Azure",
            ["Storage:Azure:ConnectionString"] = "UseDevelopmentStorage=true",
            ["Storage:Azure:Container"] = "files",
        }).Build());
        services.AddRaskStorage<StorageDbContext>();
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<AzureBlobBackend>(provider.GetRequiredService<IBlobBackend>());
    }

    private static StorageOptions S3(string? url, string bucket, string? key, string? secret)
    {
        var options = new StorageOptions { Provider = StorageProvider.S3 };
        options.S3.ServiceUrl = url is null ? null : new Uri(url);
        options.S3.Bucket = bucket;
        options.S3.AccessKeyId = key;
        options.S3.SecretAccessKey = secret;
        return options;
    }
}
