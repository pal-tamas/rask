using Microsoft.Extensions.DependencyInjection;
using Rask.Client.Browser;
using Rask.Core.Browser;

namespace Rask.Client.Tests;

// The in-process tier (WASM + Native) that AddClientBrowserApis registers: today IShare. Like the Core tier
// it is a TryAdd fallback, so a native backend (the Native host's platform module) registered first wins.
public class RaskClientBrowserApisTests
{
    [Fact]
    public void AddClientBrowserApis_RegistersShare_WithTheRequestedLifetime()
    {
        var services = new ServiceCollection();

        services.AddClientBrowserApis(ServiceLifetime.Singleton);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IShare));
        Assert.Equal(typeof(Share), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddClientBrowserApis_IsFallbackOnly_ANativeShareRegisteredFirstWins()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IShare, FakeNativeShare>();
        services.AddClientBrowserApis(ServiceLifetime.Singleton);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IShare));
        Assert.Equal(typeof(FakeNativeShare), descriptor.ImplementationType);
    }

    private sealed class FakeNativeShare : IShare
    {
        public ValueTask ShareAsync(ShareData data) => default;

        public ValueTask<bool> CanShareAsync(ShareData? data = null) => ValueTask.FromResult(true);
    }
}
