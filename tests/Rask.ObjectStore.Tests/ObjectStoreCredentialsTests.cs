using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Rask.ObjectStore.Tests;

public class ObjectStoreCredentialsTests
{
    [Fact]
    public async Task InMemory_StartsEmpty()
    {
        var credentials = new InMemoryObjectStoreCredentials();

        Assert.False(credentials.HasCredential);
        Assert.Null(await credentials.GetAsync());
    }

    [Fact]
    public async Task InMemory_ReturnsWhatWasSet()
    {
        var credentials = new InMemoryObjectStoreCredentials();

        credentials.Set(new ObjectStoreCredential("AKID", "SECRET"));

        Assert.True(credentials.HasCredential);
        Assert.Equal("AKID", (await credentials.GetAsync())!.AccessKeyId);
    }

    // The sign-out path. A credential that survived sign-out would keep working until the tab closed.
    [Fact]
    public async Task InMemory_Clear_ForgetsTheCredential()
    {
        var credentials = new InMemoryObjectStoreCredentials(new ObjectStoreCredential("AKID", "SECRET"));

        credentials.Clear();

        Assert.False(credentials.HasCredential);
        Assert.Null(await credentials.GetAsync());
    }

    // Not a style preference: the whole point of this type is that a credential cannot reach storage by
    // accident, so if a persistence hook ever appears it has to be a deliberate, reviewed change rather
    // than an overload someone reaches for. This test is what makes that deliberate.
    [Fact]
    public void InMemory_ExposesNoPersistenceHook()
    {
        var members = typeof(InMemoryObjectStoreCredentials)
            .GetMembers()
            .Select(m => m.Name)
            .ToArray();

        Assert.DoesNotContain(members, name =>
            name.Contains("Persist", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Save", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Storage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Options_ReadsAnS3KeyPair()
    {
        var credentials = new OptionsObjectStoreCredentials(Monitor(new ObjectStoreOptions
        {
            AccessKeyId = "AKID",
            SecretAccessKey = "SECRET",
            SessionToken = "TOKEN",
        }));

        var credential = await credentials.GetAsync();

        Assert.Equal("AKID", credential!.AccessKeyId);
        Assert.Equal("TOKEN", credential.SessionToken);
    }

    [Fact]
    public async Task Options_PrefersASasWhenBothArePresent()
    {
        var credentials = new OptionsObjectStoreCredentials(Monitor(new ObjectStoreOptions
        {
            AccessKeyId = "AKID",
            SecretAccessKey = "SECRET",
            SasToken = "sv=2020-02-10&sig=abc",
        }));

        Assert.Equal("sv=2020-02-10&sig=abc", (await credentials.GetAsync())!.SasToken);
    }

    [Fact]
    public async Task Options_ReturnsNull_WhenNothingIsConfigured()
    {
        Assert.Null(await new OptionsObjectStoreCredentials(Monitor(new ObjectStoreOptions())).GetAsync());
    }

    [Fact]
    public void AddInMemoryCredentials_WinsOverTheOptionsDefault()
    {
        var services = new ServiceCollection();

        services.AddRaskInMemoryObjectStoreCredentials();
        services.AddRaskS3ObjectStore(o =>
        {
            o.ServiceUrl = new Uri("https://s3.example.com");
            o.Bucket = "b";
        });

        using var provider = services.BuildServiceProvider();

        // Registered first, so the store's TryAdd fallback to configuration must not displace it — and both
        // resolutions must be the same instance, or Set(...) would populate a holder nothing reads.
        Assert.IsType<InMemoryObjectStoreCredentials>(provider.GetRequiredService<IObjectStoreCredentials>());
        Assert.Same(
            provider.GetRequiredService<InMemoryObjectStoreCredentials>(),
            provider.GetRequiredService<IObjectStoreCredentials>());
    }

    [Fact]
    public void AddS3ObjectStore_FallsBackToConfiguration()
    {
        var services = new ServiceCollection();

        services.AddRaskS3ObjectStore(o =>
        {
            o.ServiceUrl = new Uri("https://s3.example.com");
            o.Bucket = "b";
        });

        using var provider = services.BuildServiceProvider();

        Assert.IsType<OptionsObjectStoreCredentials>(provider.GetRequiredService<IObjectStoreCredentials>());
        Assert.IsType<S3ObjectStore>(provider.GetRequiredService<IObjectStore>());
    }

    [Fact]
    public void AddAzureBlobObjectStore_ResolvesTheAzureStore()
    {
        var services = new ServiceCollection();

        services.AddRaskAzureBlobObjectStore(o =>
        {
            o.ServiceUrl = new Uri("https://acct.blob.core.windows.net");
            o.Bucket = "data";
        });

        using var provider = services.BuildServiceProvider();

        Assert.IsType<AzureBlobObjectStore>(provider.GetRequiredService<IObjectStore>());
    }

    [Theory]
    [InlineData(null, "bucket")]
    [InlineData("https://s3.example.com", "")]
    public void Options_Validate_RejectsAnUnaddressableBucket(string? serviceUrl, string bucket)
    {
        var options = new ObjectStoreOptions
        {
            ServiceUrl = serviceUrl is null ? null : new Uri(serviceUrl),
            Bucket = bucket,
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static IOptionsMonitor<ObjectStoreOptions> Monitor(ObjectStoreOptions options) =>
        new StaticMonitor(options);

    private sealed class StaticMonitor(ObjectStoreOptions options) : IOptionsMonitor<ObjectStoreOptions>
    {
        public ObjectStoreOptions CurrentValue => options;

        public ObjectStoreOptions Get(string? name) => options;

        public IDisposable? OnChange(Action<ObjectStoreOptions, string?> listener) => null;
    }
}
