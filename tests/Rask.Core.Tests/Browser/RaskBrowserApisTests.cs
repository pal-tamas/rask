using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Browser;

namespace Rask.Core.Tests.Browser;

// The shared registration helper (RaskBrowserApis) is the single home for the browser/device interface →
// impl map that the Server and WASM hosts used to duplicate. These tests pin the exact Core set, its
// lifetimes, and the fallback rule: because the helper uses TryAdd, a backend the app registers first wins
// and the JS-backed wrapper is only the fallback.
public class RaskBrowserApisTests
{
    // The 38 transport-agnostic wrappers AddCoreBrowserApis must register — service type → default impl.
    // Keep in sync with the registrar; AddCoreBrowserApis_registers_nothing_beyond_the_pinned_set enforces it.
    private static readonly (Type Service, Type Impl)[] CoreApis =
    [
        (typeof(IBrowserStorage), typeof(BrowserStorage)),
        (typeof(IClipboard), typeof(Clipboard)),
        (typeof(IGeolocation), typeof(Geolocation)),
        (typeof(INavigatorInfo), typeof(NavigatorInfo)),
        (typeof(INetworkInfo), typeof(NetworkInfo)),
        (typeof(IMediaQuery), typeof(MediaQuery)),
        (typeof(ISpeechSynthesis), typeof(SpeechSynthesis)),
        (typeof(ISpeechRecognition), typeof(SpeechRecognition)),
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
        (typeof(IOriginPrivateFileSystem), typeof(OriginPrivateFileSystem)),
        (typeof(IWebAuthn), typeof(WebAuthn)),
        (typeof(ICookies), typeof(Cookies)),
        (typeof(IPermissions), typeof(Permissions)),
        (typeof(IVibration), typeof(Vibration)),
        (typeof(IPageVisibility), typeof(PageVisibilityInfo)),
        (typeof(IViewTransitions), typeof(ViewTransitions)),
        (typeof(IWebAnimations), typeof(WebAnimations)),
        (typeof(IWebLocks), typeof(WebLocks)),
        (typeof(IMediaStreams), typeof(MediaStreams)),
        (typeof(ISignaling), typeof(Signaling)),
        (typeof(IWebRtc), typeof(WebRtc)),
        (typeof(IBattery), typeof(BrowserBattery)),
        (typeof(IWebPush), typeof(Rask.Core.Browser.WebPush)),
        (typeof(INotifications), typeof(Notifications)),
        (typeof(IBadge), typeof(Badge)),
        (typeof(IWakeLock), typeof(WakeLock)),
    ];

    [Fact]
    public void AddCoreBrowserApis_registers_every_wrapper_with_its_default_impl()
    {
        var services = new ServiceCollection();

        services.AddCoreBrowserApis(ServiceLifetime.Scoped);

        foreach (var (service, impl) in CoreApis)
        {
            var descriptor = Assert.Single(services, d => d.ServiceType == service);
            Assert.Equal(impl, descriptor.ImplementationType);
        }
    }

    // The other tests here only iterate CoreApis, so a wrapper missing from the list is simply never
    // checked — the list had silently fallen three behind the registrar (ISpeechRecognition, IWebLocks,
    // IBattery shipped unverified). Compare against what AddCoreBrowserApis actually registered, so
    // adding a wrapper without pinning it fails here instead of going unnoticed.
    [Fact]
    public void AddCoreBrowserApis_registers_nothing_beyond_the_pinned_set()
    {
        var services = new ServiceCollection();

        services.AddCoreBrowserApis(ServiceLifetime.Scoped);

        var registered = services.Select(d => d.ServiceType).ToHashSet();
        var pinned = CoreApis.Select(a => a.Service).ToHashSet();

        Assert.Empty(registered.Except(pinned));   // registered but unpinned → add it to CoreApis
        Assert.Empty(pinned.Except(registered));   // pinned but unregistered → stale entry
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Singleton)]
    public void AddCoreBrowserApis_uses_the_requested_lifetime(ServiceLifetime lifetime)
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
    public void AddCoreBrowserApis_does_not_register_in_process_or_WASM_only_APIs()
    {
        var services = new ServiceCollection();

        services.AddCoreBrowserApis(ServiceLifetime.Scoped);

        // IShare and the WASM-only device set both live in Rask.Wasm. Neither is in the Core tier.
        Assert.DoesNotContain(services, d => d.ServiceType.Name is "IShare" or "IFullscreen" or "ISerial");
    }

    [Fact]
    public void A_browser_API_is_only_a_fallback_so_an_app_backend_registered_first_wins()
    {
        var services = new ServiceCollection();

        // The app registers its own backend first...
        services.AddSingleton<IClipboard, FakeAppClipboard>();
        // ...then the framework wires the JS-backed fallbacks.
        services.AddCoreBrowserApis(ServiceLifetime.Scoped);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IClipboard));
        Assert.Equal(typeof(FakeAppClipboard), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime); // the app registration's lifetime, untouched
    }

    [Fact]
    public void An_unclaimed_browser_API_registers_the_JS_wrapper()
    {
        var services = new ServiceCollection();

        services.AddCoreBrowserApis(ServiceLifetime.Scoped);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IClipboard));
        Assert.Equal(typeof(Clipboard), descriptor.ImplementationType);
    }

    private sealed class FakeAppClipboard : IClipboard
    {
        public ValueTask WriteTextAsync(string text) => default;

        public ValueTask<string> ReadTextAsync() => ValueTask.FromResult(string.Empty);
    }
}
