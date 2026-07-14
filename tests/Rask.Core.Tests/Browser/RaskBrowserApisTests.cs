using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Browser;

namespace Rask.Core.Tests.Browser;

// The shared registration helper (RaskBrowserApis) is the single home for the browser/device interface →
// impl map that the Server/WASM/Native hosts used to duplicate. These tests pin the exact Core set, its
// lifetimes, and the native-first rule: because the helper uses TryAdd, a backend registered first (by a
// native platform or the app) wins and the JS-backed wrapper is only the fallback.
public class RaskBrowserApisTests
{
    // The 31 transport-agnostic wrappers AddCoreBrowserApis must register — service type → default impl.
    private static readonly (Type Service, Type Impl)[] CoreApis =
    [
        (typeof(IBrowserStorage), typeof(BrowserStorage)),
        (typeof(IClipboard), typeof(Clipboard)),
        (typeof(IGeolocation), typeof(Geolocation)),
        (typeof(INavigatorInfo), typeof(NavigatorInfo)),
        (typeof(INetworkInfo), typeof(NetworkInfo)),
        (typeof(IMediaQuery), typeof(MediaQuery)),
        (typeof(ISpeechSynthesis), typeof(SpeechSynthesis)),
        (typeof(IScreenInfo), typeof(ScreenInfoReader)),
        (typeof(IStorageEstimator), typeof(StorageEstimator)),
        (typeof(IVisualViewport), typeof(VisualViewportReader)),
        (typeof(IBroadcastChannel), typeof(BroadcastChannelService)),
        (typeof(IIntersectionObserver), typeof(IntersectionObserverService)),
        (typeof(IResizeObserver), typeof(ResizeObserverService)),
        (typeof(IMutationObserver), typeof(MutationObserverService)),
        (typeof(IMediaSession), typeof(MediaSession)),
        (typeof(IGamepad), typeof(Gamepad)),
        (typeof(IDeviceOrientation), typeof(DeviceOrientation)),
        (typeof(IDeviceMotion), typeof(DeviceMotion)),
        (typeof(ICrypto), typeof(Crypto)),
        (typeof(IPerformance), typeof(Rask.Core.Browser.Performance)),
        (typeof(IIndexedDb), typeof(IndexedDb)),
        (typeof(IFileSystemAccess), typeof(FileSystemAccess)),
        (typeof(IWebAuthn), typeof(WebAuthn)),
        (typeof(ICookies), typeof(Cookies)),
        (typeof(IPermissions), typeof(Permissions)),
        (typeof(IVibration), typeof(Vibration)),
        (typeof(IPageVisibility), typeof(PageVisibilityInfo)),
        (typeof(IWebPush), typeof(WebPush)),
        (typeof(INotifications), typeof(Notifications)),
        (typeof(IBadge), typeof(Badge)),
        (typeof(IWakeLock), typeof(WakeLock)),
    ];

    [Fact]
    public void AddCoreBrowserApis_RegistersEveryWrapper_WithDefaultImpl()
    {
        var services = new ServiceCollection();

        services.AddCoreBrowserApis(ServiceLifetime.Scoped);

        foreach (var (service, impl) in CoreApis)
        {
            var descriptor = Assert.Single(services, d => d.ServiceType == service);
            Assert.Equal(impl, descriptor.ImplementationType);
        }
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Singleton)]
    public void AddCoreBrowserApis_UsesTheRequestedLifetime(ServiceLifetime lifetime)
    {
        var services = new ServiceCollection();

        services.AddCoreBrowserApis(lifetime);

        foreach (var (service, _) in CoreApis)
        {
            var descriptor = Assert.Single(services, d => d.ServiceType == service);
            Assert.Equal(lifetime, descriptor.Lifetime);
        }
    }

    [Fact]
    public void AddCoreBrowserApis_DoesNotRegisterInProcessOrWasmOnlyApis()
    {
        var services = new ServiceCollection();

        services.AddCoreBrowserApis(ServiceLifetime.Scoped);

        // IShare lives in Rask.Client; the WASM-only device set lives in Rask.Wasm. Neither is in the Core tier.
        Assert.DoesNotContain(services, d => d.ServiceType.Name is "IShare" or "IFullscreen" or "ISerial");
    }

    [Fact]
    public void AddBrowserApi_IsFallbackOnly_ANativeBackendRegisteredFirstWins()
    {
        var services = new ServiceCollection();

        // A native platform (or the app) registers its backend first...
        services.AddSingleton<IClipboard, FakeNativeClipboard>();
        // ...then the framework wires the JS-backed fallbacks.
        services.AddCoreBrowserApis(ServiceLifetime.Scoped);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IClipboard));
        Assert.Equal(typeof(FakeNativeClipboard), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime); // the native registration's lifetime, untouched
    }

    [Fact]
    public void AddBrowserApi_WhenUnclaimed_RegistersTheJsWrapper()
    {
        var services = new ServiceCollection();

        services.AddCoreBrowserApis(ServiceLifetime.Scoped);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IClipboard));
        Assert.Equal(typeof(Clipboard), descriptor.ImplementationType);
    }

    private sealed class FakeNativeClipboard : IClipboard
    {
        public ValueTask WriteTextAsync(string text) => default;

        public ValueTask<string> ReadTextAsync() => ValueTask.FromResult(string.Empty);
    }
}
