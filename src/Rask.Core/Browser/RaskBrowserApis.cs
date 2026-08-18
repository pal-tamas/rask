using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Core.Browser;

/// <summary>
///     Central registration for the typed browser/device API wrappers. Each host calls the tier helper for
///     the wrappers it can serve (Server → <see cref="AddCoreBrowserApis" />; WASM → all three tiers; Native
///     → core + client), instead of hand-maintaining the interface → impl list in three places.
/// </summary>
/// <remarks>
///     Every wrapper is registered with <see cref="ServiceCollectionDescriptorExtensions.TryAdd(IServiceCollection,ServiceDescriptor)" />,
///     so the JS-backed wrapper is a <em>fallback</em>: a host (or a platform head, or the app itself) that
///     has a better implementation registers it <b>first</b> and wins. That is how the framework picks the
///     best implementation per platform — a native iOS/Android backend where one exists, the WebView/JS
///     wrapper otherwise — with no app-head wiring. The registrations use compile-time <c>typeof</c> only:
///     no reflection, trim-safe.
/// </remarks>
public static class RaskBrowserApis
{
    /// <summary>
    ///     Registers one wrapper as a fallback (<c>TryAdd</c>) at <paramref name="lifetime" />: the mapping is
    ///     applied only if <typeparamref name="TService" /> is not already registered, so an earlier
    ///     (native / app) registration wins.
    /// </summary>
    public static IServiceCollection AddBrowserApi<TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImpl>(
        this IServiceCollection services, ServiceLifetime lifetime)
        where TService : class
        where TImpl : class, TService
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAdd(ServiceDescriptor.Describe(typeof(TService), typeof(TImpl), lifetime));
        return services;
    }

    /// <summary>
    ///     Registers the transport-agnostic <see cref="Rask.Core.Browser" /> wrappers — the set that works on
    ///     every host (Server, WASM, Native) because each is <see cref="Microsoft.JSInterop.IJSRuntime" />-backed
    ///     and needs no transient user activation. Server uses <see cref="ServiceLifetime.Scoped" /> (one per
    ///     WebSocket session); the in-process hosts use <see cref="ServiceLifetime.Singleton" />.
    /// </summary>
    public static IServiceCollection AddCoreBrowserApis(this IServiceCollection services, ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBrowserApi<IBrowserStorage, BrowserStorage>(lifetime);
        services.AddBrowserApi<IClipboard, Clipboard>(lifetime);
        services.AddBrowserApi<IGeolocation, Geolocation>(lifetime);
        services.AddBrowserApi<INavigatorInfo, NavigatorInfo>(lifetime);
        services.AddBrowserApi<INetworkInfo, NetworkInfo>(lifetime);
        services.AddBrowserApi<IMediaQuery, MediaQuery>(lifetime);
        services.AddBrowserApi<ISpeechSynthesis, SpeechSynthesis>(lifetime);
        services.AddBrowserApi<ISpeechRecognition, SpeechRecognition>(lifetime);
        services.AddBrowserApi<IScreenInfo, ScreenInfoReader>(lifetime);
        services.AddBrowserApi<IStorageEstimator, StorageEstimator>(lifetime);
        services.AddBrowserApi<IVisualViewport, VisualViewportReader>(lifetime);
        services.AddBrowserApi<IBroadcastChannel, BroadcastChannelService>(lifetime);
        services.AddBrowserApi<IIntersectionObserver, IntersectionObserverService>(lifetime);
        services.AddBrowserApi<IResizeObserver, ResizeObserverService>(lifetime);
        services.AddBrowserApi<IMutationObserver, MutationObserverService>(lifetime);
        services.AddBrowserApi<IMediaSession, MediaSession>(lifetime);
        services.AddBrowserApi<IGamepad, Gamepad>(lifetime);
        services.AddBrowserApi<IDeviceOrientation, DeviceOrientation>(lifetime);
        services.AddBrowserApi<IDeviceMotion, DeviceMotion>(lifetime);
        services.AddBrowserApi<ICrypto, Crypto>(lifetime);
        services.AddBrowserApi<IPerformance, Performance>(lifetime);
        services.AddBrowserApi<IIndexedDb, IndexedDb>(lifetime);
        services.AddBrowserApi<IFileSystemAccess, FileSystemAccess>(lifetime);
        services.AddBrowserApi<IOriginPrivateFileSystem, OriginPrivateFileSystem>(lifetime);
        services.AddBrowserApi<IWebAuthn, WebAuthn>(lifetime);
        services.AddBrowserApi<ICookies, Cookies>(lifetime);
        services.AddBrowserApi<IPermissions, Permissions>(lifetime);
        services.AddBrowserApi<IVibration, Vibration>(lifetime);
        services.AddBrowserApi<IPageVisibility, PageVisibilityInfo>(lifetime);
        services.AddBrowserApi<IViewTransitions, ViewTransitions>(lifetime);
        services.AddBrowserApi<IWebLocks, WebLocks>(lifetime);
        services.AddBrowserApi<IMediaStreams, MediaStreams>(lifetime);
        services.AddBrowserApi<ISignaling, Signaling>(lifetime);
        services.AddBrowserApi<IWebRtc, WebRtc>(lifetime);
        services.AddBrowserApi<IBattery, Battery>(lifetime);
        // Transport-agnostic PWA APIs (IJSRuntime-backed, no transient activation): push subscribe, local
        // notifications, app badge, screen wake lock. Their JS helpers ship on Server only under AddRaskPwa.
        services.AddBrowserApi<IWebPush, WebPush>(lifetime);
        services.AddBrowserApi<INotifications, Notifications>(lifetime);
        services.AddBrowserApi<IBadge, Badge>(lifetime);
        services.AddBrowserApi<IWakeLock, WakeLock>(lifetime);
        return services;
    }
}
