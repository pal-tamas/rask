using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Rask.Storage.Backends;

namespace Rask.Storage.Tests;

/// <summary>
///     The S3 and Azure stores against real implementations — MinIO and Azurite — because a signature is either
///     exactly right or rejected, and only a real service says which. Unit tests pin the strings; this proves a
///     service accepts them.
/// </summary>
/// <remarks>
///     By hand, never in a hook: <c>scripts/run-storage-providers-local.sh</c> starts both stores in containers and
///     sets <c>RASK_STORAGE_PROVIDERS=1</c>. Without it every test here reports SKIPPED rather than passing.
/// </remarks>
[Collection(StorageDbCollection.Name)]
public sealed class ProviderSmokeTests
{
    private const string SkipReason =
        "Storage provider gate: run scripts/run-storage-providers-local.sh (it starts MinIO and Azurite and sets RASK_STORAGE_PROVIDERS=1).";

    private const string Bucket = "rask-smoke";

    private static bool Enabled => Environment.GetEnvironmentVariable("RASK_STORAGE_PROVIDERS") == "1";

    private static string S3Url => Environment.GetEnvironmentVariable("RASK_STORAGE_S3_URL") ?? "http://127.0.0.1:19000";

    private static string S3Key => Environment.GetEnvironmentVariable("RASK_STORAGE_S3_KEY") ?? "raskminio";

    private static string S3Secret => Environment.GetEnvironmentVariable("RASK_STORAGE_S3_SECRET") ?? "raskminiosecret";

    [SkippableFact]
    public async Task S3_round_trips_a_file_and_its_presigned_url_opens()
    {
        Skip.IfNot(Enabled, SkipReason);
        await CreateBucketAsync();

        await using var harness = new StorageHarness(o =>
        {
            o.Provider = StorageProvider.S3;
            o.S3.ServiceUrl = new Uri(S3Url);
            o.S3.Bucket = Bucket;
            o.S3.AccessKeyId = S3Key;
            o.S3.SecretAccessKey = S3Secret;
        });

        await RoundTripAsync(harness);
    }

    [SkippableFact]
    public async Task Azure_round_trips_a_file_and_its_sas_url_opens()
    {
        Skip.IfNot(Enabled, SkipReason);
        await CreateContainerAsync();

        await using var harness = new StorageHarness(o =>
        {
            o.Provider = StorageProvider.Azure;
            o.Azure.ConnectionString = (Environment.GetEnvironmentVariable("RASK_STORAGE_AZURE") ?? "UseDevelopmentStorage=true");
            o.Azure.Container = Bucket;
        });

        await RoundTripAsync(harness);
    }

    private static async Task RoundTripAsync(StorageHarness harness)
    {
        var bytes = Samples.Png(300_000);
        var file = await harness.Files.SaveAsync(new MemoryStream(bytes), "smoke  photo.png");

        // Read back through the store.
        await using (var stream = await harness.Files.OpenReadAsync(file.Id))
        {
            var copy = new MemoryStream();
            await stream!.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }

        // A range through the serving stream: the store must honour the offset and count.
        await using (var ranged = await harness.Runtime.Backend.OpenForServingAsync(file.Key, file.Size, 1000, 1999, default))
        {
            ranged!.Seek(1000, SeekOrigin.Begin);
            var slice = new byte[1000];
            await ranged.ReadExactlyAsync(slice);
            Assert.Equal(bytes[1000..2000], slice);
        }

        // The provider-signed URL, fetched by a client that is not the app, with the safe headers it signed.
        var url = await harness.Files.TemporaryUrlAsync(file.Id, TimeSpan.FromMinutes(5));
        Assert.NotNull(url);
        Assert.StartsWith("http", url);
        using (var client = new HttpClient())
        {
            using var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);

            using var tampered = await client.GetAsync(url!.Replace("smoke", "smoky", StringComparison.Ordinal)
                                                         + (url.Contains('?', StringComparison.Ordinal) ? "&x=1" : ""));
            Assert.NotEqual(HttpStatusCode.OK, tampered.StatusCode);
        }

        // The listing the sweep reads finds it, and delete removes it.
        var keys = await harness.Runtime.Backend.ListAsync("", default).Select(e => e.Key).ToListAsync();
        Assert.Contains(file.Key, keys);

        Assert.True(await harness.Files.DeleteAsync(file.Id));
        Assert.Null(await harness.Runtime.Backend.OpenReadAsync(file.Key, 0, null, default));
    }

    private static async Task CreateBucketAsync()
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{S3Url.TrimEnd('/')}/{Bucket}");
        SigV4.SignHeaders(request, "/" + Bucket, "", new S3Credential(S3Key, S3Secret, null), "us-east-1", DateTimeOffset.UtcNow);
        using var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict,
            $"Creating the MinIO bucket answered {(int)response.StatusCode}.");
    }

    private static async Task CreateContainerAsync()
    {
        var account = AzureAccount.Parse((Environment.GetEnvironmentVariable("RASK_STORAGE_AZURE") ?? "UseDevelopmentStorage=true"));
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{account.BlobEndpoint}/{Bucket}?restype=container")
        {
            Content = new ByteArrayContent([]),
        };
        request.Content.Headers.ContentLength = 0;
        AzureSharedKey.Sign(request, account, DateTimeOffset.UtcNow);
        using var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict,
            $"Creating the Azurite container answered {(int)response.StatusCode}.");
    }
}
