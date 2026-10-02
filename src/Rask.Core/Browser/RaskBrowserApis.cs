using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Core.Browser;

/// <summary>
///     Central registration for the typed browser/device API wrappers. Each host calls the tier helper for
///     the wrappers it can serve (Server → <see cref="AddCoreBrowserApis" />; WASM → both tiers), instead
///     of hand-maintaining the interface → impl list in several places.
/// </summary>
/// <remarks>
///     Every wrapper is registered with <see cref="ServiceCollectionDescriptorExtensions.TryAdd(IServiceCollection,ServiceDescriptor)" />,
///     so the JS-backed wrapper is a <em>fallback</em>: a host (or the app itself) that has a better
///     implementation registers it <b>first</b> and wins, which is how an app substitutes its own backend
///     for a wrapper without touching the host wiring. The registrations use compile-time <c>typeof</c>
///     only: no reflection, trim-safe.
/// </remarks>
public static class RaskBrowserApis
{
    /// <summary>
    ///     Registers one wrapper as a fallback (<c>TryAdd</c>) at <paramref name="lifetime" />: the mapping is
    ///     applied only if <typeparamref name="TService" /> is not already registered, so an earlier
    ///     app registration wins.
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
    ///     every host (Server and WASM) because each is <see cref="Microsoft.JSInterop.IJSRuntime" />-backed
    ///     and needs no transient user activation. Server uses <see cref="ServiceLifetime.Scoped" /> (one per
    ///     WebSocket session); the in-process hosts use <see cref="ServiceLifetime.Singleton" />.
    /// </summary>
    public static IServiceCollection AddCoreBrowserApis(this IServiceCollection services, ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPageAndDeviceApis(services, lifetime);
        AddObserverAndMediaApis(services, lifetime);
        AddStorageAndSecurityApis(services, lifetime);
        AddPwaApis(services, lifetime);
        return services;
    }

    private static void AddPageAndDeviceApis(IServiceCollection services, ServiceLifetime lifetime)
    {
        services.AddBrowserApi<IDeviceOrientation, DeviceOrientation>(lifetime);
        services.AddBrowserApi<IDeviceMotion, DeviceMotion>(lifetime);
        services.AddBrowserApi<IViewTransitions, ViewTransitions>(lifetime);
        services.AddBrowserApi<IWebAnimations, WebAnimations>(lifetime);
    }

    private static void AddObserverAndMediaApis(IServiceCollection services, ServiceLifetime lifetime)
    {
        services.AddBrowserApi<ISpeechSynthesis, SpeechSynthesis>(lifetime);
        services.AddBrowserApi<ISpeechRecognition, SpeechRecognition>(lifetime);
        services.AddBrowserApi<IMediaStreams, MediaStreams>(lifetime);
        services.AddBrowserApi<ISignaling, Signaling>(lifetime);
        services.AddBrowserApi<IWebRtc, WebRtc>(lifetime);
    }

    private static void AddStorageAndSecurityApis(IServiceCollection services, ServiceLifetime lifetime)
    {
        services.AddBrowserApi<IStorageEstimator, StorageEstimator>(lifetime);
        services.AddBrowserApi<IIndexedDb, IndexedDb>(lifetime);
        services.AddBrowserApi<ICookies, Cookies>(lifetime);
        services.AddBrowserApi<IWebLocks, WebLocks>(lifetime);
        services.AddBrowserApi<IWebAuthn, WebAuthn>(lifetime);
    }

    // Transport-agnostic PWA APIs (IJSRuntime-backed, no transient activation): push subscribe, local
    // notifications, app badge, screen wake lock. Their JS helpers ship on Server only under AddRaskPwa.
    private static void AddPwaApis(IServiceCollection services, ServiceLifetime lifetime)
    {
        services.AddBrowserApi<IWebPush, WebPush>(lifetime);
        services.AddBrowserApi<IWakeLock, WakeLock>(lifetime);
    }
}
