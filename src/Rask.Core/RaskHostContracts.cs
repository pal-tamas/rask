using Microsoft.JSInterop;
using Rask.Core.Authentication;
using Rask.Core.Browser;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Core.Messaging;
using Rask.Core.Routing;

namespace Rask.Core;

/// <summary>
///     The contracts <c>Rask.Core</c> guarantees are resolvable on <b>every</b> host — Server and WASM.
///     Core is the shared component surface: anything a component can inject or call from Core must work on
///     both, or a component shared between an app's hosts breaks on one of them only, at runtime, with no
///     compile-time signal.
/// </summary>
/// <remarks>
///     <para>
///         This list is the machine-readable form of that rule. Each host's test project asserts that its own
///         bootstrap (<c>AddRask</c> / <c>WasmHostBuilder</c>) resolves every entry, so a host that forgets
///         one fails the build instead of shipping a hole. That gate is the reason it exists: all three of
///         <see cref="IBrowserFileBackend" />, <see cref="IDownloadSink" /> and <see cref="IAuthSignIn" />
///         had silently drifted off a host before it was added.
///     </para>
///     <para>
///         <see cref="BrowserApis" /> mirrors <see cref="RaskBrowserApis.AddCoreBrowserApis" /> — the
///         transport-agnostic wrapper tier every host serves. It is spelled out rather than reflected so the
///         registration helper can keep its trim-safe generic <c>TryAdd</c> calls; a completeness test in
///         <c>Rask.Core.Tests</c> asserts this list plus <see cref="NonServiceBrowserTypes" /> covers every
///         public interface in the <c>Rask.Core.Browser</c> namespace, so adding a wrapper forces a decision
///         here rather than silently landing on one host.
///     </para>
/// </remarks>
public static class RaskHostContracts
{
    /// <summary>
    ///     The per-session/per-app services each host wires by hand. These are where drift actually happens:
    ///     unlike <see cref="BrowserApis" /> there is no shared registration helper forcing both hosts to
    ///     agree, so each one spells them out in its own bootstrap.
    /// </summary>
    public static IReadOnlyList<Type> HostServices { get; } =
    [
        // Routing. Navigator additionally needs IDownloadSink below for Navigator.Download to work.
        typeof(RouteState),
        typeof(Navigator),
        // Declared state, transient user messages, and the current user.
        typeof(IPersistentState),
        typeof(IToaster),
        typeof(IUserProvider),
        // Sign-in/out. The two hosts mean very different things by it (a cookie the server sets, or a POST
        // to a logout endpoint) — which is exactly why each must supply one.
        typeof(IAuthSignIn),
        // File input (<input type=file> -> RaskFile) and Navigator.Download.
        typeof(IBrowserFileBackend),
        typeof(IDownloadSink),
        // The interop runtime every Core browser wrapper is built on.
        typeof(IJSRuntime),
    ];

    /// <summary>
    ///     The transport-agnostic browser/device wrappers from <see cref="RaskBrowserApis.AddCoreBrowserApis" />.
    ///     Every host serves all of them: each is <see cref="IJSRuntime" />-backed and needs no transient user
    ///     activation, so none of them depends on being in-process. A host or the app may register a better
    ///     implementation first (<c>TryAdd</c> makes the JS wrapper the fallback), but it may not register
    ///     none.
    /// </summary>
    public static IReadOnlyList<Type> BrowserApis { get; } =
    [
        typeof(IBadge), typeof(IBattery), typeof(IBroadcastChannel), typeof(IBrowserStorage),
        typeof(IClipboard), typeof(ICookies), typeof(ICrypto), typeof(IDeviceMotion),
        typeof(IDeviceOrientation), typeof(IFileSystemAccess), typeof(IGamepad), typeof(IGeolocation),
        typeof(IIndexedDb), typeof(IIntersectionObserver), typeof(IMediaQuery), typeof(IMediaSession),
        typeof(IMediaStreams), typeof(IMutationObserver), typeof(INavigatorInfo), typeof(INetworkInfo),
        typeof(INotifications), typeof(IOriginPrivateFileSystem), typeof(IPageVisibility),
        typeof(IPerformance), typeof(IPermissions), typeof(IResizeObserver), typeof(IScreenInfo),
        typeof(ISignaling), typeof(ISpeechRecognition), typeof(ISpeechSynthesis), typeof(IStorageEstimator),
        typeof(IVibration), typeof(IViewTransitions), typeof(IVisualViewport), typeof(IWakeLock),
        typeof(IWebAnimations), typeof(IWebAuthn), typeof(IWebLocks), typeof(IWebPush), typeof(IWebRtc),
    ];

    /// <summary>
    ///     Public interfaces in <c>Rask.Core.Browser</c> that are <b>not</b> DI services: handles and
    ///     connections a wrapper hands back (an open channel, a picked file, a held lock). They are created by
    ///     the service that returns them, never resolved from the container — so the parity gate must not
    ///     expect a registration for them. Listed explicitly so the completeness test can tell "deliberately
    ///     not a service" apart from "forgot to register".
    /// </summary>
    public static IReadOnlyList<Type> NonServiceBrowserTypes { get; } =
    [
        typeof(IBroadcastChannelConnection), typeof(IDirectoryHandle), typeof(IFileHandle),
        typeof(IKeyValueStore), typeof(IPeerConnection), typeof(IRtcDataChannel),
        typeof(ISignalingConnection), typeof(IWakeLockSentinel), typeof(IWebStorage),
    ];

    /// <summary>
    ///     Everything a host must resolve: <see cref="HostServices" /> followed by <see cref="BrowserApis" />.
    ///     This is what the per-host parity tests iterate.
    /// </summary>
    public static IReadOnlyList<Type> All { get; } = [.. HostServices, .. BrowserApis];
}
