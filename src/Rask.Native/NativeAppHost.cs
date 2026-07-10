using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Rask.Client.Browser;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Browser;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.Messaging;
using Rask.Core.Routing;

namespace Rask.Native;

/// <summary>
///     Entry point for a native-mobile Rask app. Mirrors <c>WasmHostBuilder</c> / the Server's
///     <c>AddRask</c>: register app services on <see cref="Services" />, then run in one of two modes:
///     <list type="bullet">
///         <item>
///             <b>Native + Local</b> (<see cref="RunLocalAsync{TApp}" />) — the app runs in-process on the
///             device and drives a platform WebView through <see cref="INativeWebView" />, using the same
///             render → diff pipeline as the other hosts. This is the offline, store-distributable native app.
///         </item>
///         <item>
///             <b>Native + Server</b> (<see cref="ConnectToServer" />) — the WebView is a thin native shell
///             over a remote Rask Server: it loads the server's own <c>rask.js</c> and connects over
///             <c>wss://</c>. No in-process session; the platform head just points its WebView at the URL.
///         </item>
///     </list>
/// </summary>
public sealed class NativeAppHost
{
    // The wire-payload shape the in-process session renders with, snapshotted from RaskLiveOptions in
    // CreateDefault and handed to the NativeLiveSession — a per-session value instead of the former
    // process-global LiveOptions.DiffMode static. Native fans render continuations onto the thread pool,
    // so a shared mutable static would be doubly wrong here; carrying it on the session also lets the
    // native session tests run without serializing on the global.
    private LiveDiffMode _diffMode = LiveDiffMode.Auto;

    private NativeAppHost()
    {
        Services = new ServiceCollection();
        Services.AddLogging();
        Services.AddSingleton<RouteState>();
        Services.AddSingleton<Navigator>();
        // Singleton = one queue for the app instance (the whole native app is a single session), so a
        // message queued before a NavigateTo survives it. Same model as WasmHostBuilder.
        Services.AddSingleton<IFlash, Flash>();

        // The transport-agnostic browser-API surface (Rask.Core.Browser) — every wrapper is IJSRuntime-
        // backed and works through the WebView's JS engine. These are the default backings; a platform
        // head can replace any of them with a native C# backend by registering its own implementation on
        // host.Services before RunLocalAsync (last registration wins) — see IShare below and docs/native.md.
        // The remaining WASM-only wrappers (IFullscreen, device APIs) live in Rask.Wasm and are not
        // referenced here.
        Services.AddSingleton<IBrowserStorage, BrowserStorage>();
        Services.AddSingleton<IClipboard, Clipboard>();
        Services.AddSingleton<IGeolocation, Geolocation>();
        Services.AddSingleton<INavigatorInfo, NavigatorInfo>();
        Services.AddSingleton<INetworkInfo, NetworkInfo>();
        Services.AddSingleton<IMediaQuery, MediaQuery>();
        Services.AddSingleton<ISpeechSynthesis, SpeechSynthesis>();
        Services.AddSingleton<IScreenInfo, ScreenInfoReader>();
        Services.AddSingleton<IStorageEstimator, StorageEstimator>();
        Services.AddSingleton<IVisualViewport, VisualViewportReader>();
        Services.AddSingleton<IBroadcastChannel, BroadcastChannelService>();
        Services.AddSingleton<IIntersectionObserver, IntersectionObserverService>();
        Services.AddSingleton<IResizeObserver, ResizeObserverService>();
        Services.AddSingleton<IMutationObserver, MutationObserverService>();
        Services.AddSingleton<IMediaSession, MediaSession>();
        Services.AddSingleton<IGamepad, Gamepad>();
        Services.AddSingleton<IDeviceOrientation, DeviceOrientation>();
        Services.AddSingleton<IDeviceMotion, DeviceMotion>();
        Services.AddSingleton<ICrypto, Crypto>();
        Services.AddSingleton<IPerformance, Performance>();
        Services.AddSingleton<IIndexedDb, IndexedDb>();
        Services.AddSingleton<IFileSystemAccess, FileSystemAccess>();
        Services.AddSingleton<IWebAuthn, WebAuthn>();
        Services.AddSingleton<ICookies, Cookies>();
        Services.AddSingleton<IPermissions, Permissions>();
        Services.AddSingleton<IVibration, Vibration>();
        Services.AddSingleton<IPageVisibility, PageVisibilityInfo>();
        // Share: JS-backed default (navigator.share). On a real device a platform head registers a native
        // backend (UIActivityViewController / Intent.ACTION_SEND) over this before RunLocalAsync — the
        // native path needs no user activation and works where the WebView lacks navigator.share.
        Services.AddSingleton<IShare, Share>();
        // Transport-agnostic PWA APIs (IJSRuntime-backed) — push subscribe, local notifications, app badge,
        // wake lock — work in the WebView too, like on Server.
        Services.AddSingleton<IWebPush, WebPush>();
        Services.AddSingleton<INotifications, Notifications>();
        Services.AddSingleton<IBadge, Badge>();
        Services.AddSingleton<IWakeLock, WakeLock>();

        Services.TryAddSingleton<IUserProvider, AnonymousUserProvider>();
        Services.AddAuthorizationCore();

        // IJSRuntime backed by the native WebView bridge. Singleton — one runtime per app instance;
        // NativeLiveSession's ctor binds it to the session + WebView.
        Services.AddSingleton<NativeJSRuntime>();
        Services.AddSingleton<IJSRuntime>(sp => sp.GetRequiredService<NativeJSRuntime>());
    }

    /// <summary>The DI container for the app. Register your services here before running.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Creates a host with framework-default live options (<see cref="LiveDiffMode.Auto" />).</summary>
    public static NativeAppHost CreateDefault() => CreateDefault(null);

    /// <summary>Creates a host, optionally overriding the diff mode (e.g. <c>o =&gt; o.DiffMode = LiveDiffMode.DisabledFull</c>).</summary>
    public static NativeAppHost CreateDefault(Action<RaskLiveOptions>? configureLive)
    {
        var host = new NativeAppHost();
        if (configureLive is not null)
        {
            var opts = new RaskLiveOptions();
            configureLive(opts);
            host._diffMode = opts.DiffMode;
        }

        return host;
    }

    /// <summary>
    ///     <b>Native + Server.</b> Validate a remote Rask Server URL for the WebView to load. The platform
    ///     head navigates its WebView here; the server serves its own <c>rask.js</c> client, which connects
    ///     back over <c>wss://</c>. No in-process session — the device is a thin, store-distributable shell
    ///     with native device APIs available to the loaded page.
    /// </summary>
    public static NativeServerShell ConnectToServer(Uri serverBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(serverBaseUrl);
        if (!serverBaseUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("The Rask Server URL must be absolute (e.g. https://app.example.com/).",
                nameof(serverBaseUrl));
        }

        return new NativeServerShell(serverBaseUrl);
    }

    /// <summary>
    ///     <b>Native + Local.</b> Build the service provider, instantiate <typeparamref name="TApp" />
    ///     (wrapped in a <see cref="RootErrorBoundary" />), seed the initial route, create the in-process
    ///     <see cref="NativeLiveSession" />, and wire the <paramref name="webView" /> message channel. The
    ///     first render is pushed when the WebView's client posts its <c>ready</c> message, so this is safe
    ///     to call before the WebView has finished loading the shell.
    /// </summary>
    /// <typeparam name="TApp">The root <see cref="Component" />. Must render a complete page shell (RASK021).</typeparam>
    public async Task<NativeApp> RunLocalAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        INativeWebView webView, string initialPath = "/")
        where TApp : Component
    {
        ArgumentNullException.ThrowIfNull(webView);

        var provider = Services.BuildServiceProvider();

        var app = ActivatorUtilities.CreateInstance<TApp>(provider);
        var root = new RootErrorBoundary(app);

        var routeState = provider.GetRequiredService<RouteState>();
        SeedRoute(routeState, initialPath);

        if (provider.GetService<IUserProvider>() is { } userProvider)
        {
            try { await userProvider.EnsureLoadedAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                RaskDiagnostics.Report(RaskLogLevel.Error, "Rask.Native",
                    "[Rask.Native] IUserProvider.EnsureLoadedAsync failed", ex);
            }
        }

        var session = new NativeLiveSession(root, provider, webView, _diffMode);
        var nativeApp = new NativeApp(session, provider);

        // The WebView drives everything through this single message channel: the initial-render handshake
        // (ready), IJSRuntime results (jsResult), JS-initiated [JSInvokable] (dotNetInvoke), and component
        // events (everything else → the session). Mirrors how the WASM host multiplexes its JSExports.
        webView.OnMessage = json => RouteMessageAsync(nativeApp, json);

        return nativeApp;
    }

    // Seed the initial route from the app's start path (native apps have no browser location). Splits an
    // optional query and defaults to "/". Mirrors Rask.Wasm's RouteSeeder minus the /index.html handling.
    private static void SeedRoute(RouteState state, string initialPath)
    {
        try
        {
            var location = initialPath ?? string.Empty;
            var qIndex = location.IndexOf('?');
            var path = qIndex < 0 ? location : location[..qIndex];
            var query = qIndex < 0 ? string.Empty : location[qIndex..];
            if (path.Length == 0)
            {
                path = "/";
            }

            state.Path = path;
            state.Query = string.IsNullOrEmpty(query) ? QueryCollection.Empty : QueryString.Parse(query);
        }
        catch
        {
            state.Path = "/";
            state.Query = QueryCollection.Empty;
        }
    }

    private static async Task RouteMessageAsync(NativeApp app, byte[] json)
    {
        if (json is null || json.Length == 0)
        {
            return;
        }

        string? type;
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json.AsMemory());
            root = doc.RootElement.Clone();
            type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;
        }
        catch (JsonException ex)
        {
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Native",
                "[Rask.Native] discarded a malformed WebView message", ex);
            return;
        }

        switch (type)
        {
            case "ready":
                // First-render handshake: the client signals it has loaded and is ready to receive frames.
                await app.Session.InitialRenderAsync().ConfigureAwait(false);
                return;
            case "jsResult":
                HandleJsResult(app, root);
                return;
            case "dotNetInvoke":
                HandleDotNetInvoke(app, root);
                return;
            case "capability":
                // A client capability invoke (window.__raskNative.invoke). Routes a native device capability
                // (currently "share") to the registered service, so a declarative Shareable / a native-shell
                // page reaches the native backend the head registered — see docs/native.md.
                await HandleCapabilityAsync(app, root).ConfigureAwait(false);
                return;
            default:
                await app.Session.DispatchAsync(json).ConfigureAwait(false);
                return;
        }
    }

    // Repackage { type:"jsResult", id:<long>, success:<bool>, result?|error? } as the
    // [taskId, success, result|error] triple DotNetDispatcher.EndInvokeJS expects — identical to the
    // Server host's HandleJsResult.
    private static void HandleJsResult(NativeApp app, JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number
            || !idEl.TryGetInt64(out var taskId))
        {
            return;
        }

        var success = root.TryGetProperty("success", out var sEl) && sEl.ValueKind == JsonValueKind.True;
        var runtime = app.Services.GetService<NativeJSRuntime>();
        if (runtime is null)
        {
            return;
        }

        using var stream = new MemoryStream(128);
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartArray();
            w.WriteNumberValue(taskId);
            w.WriteBooleanValue(success);
            if (success)
            {
                if (root.TryGetProperty("result", out var resEl)) { resEl.WriteTo(w); }
                else { w.WriteNullValue(); }
            }
            else
            {
                w.WriteStringValue(
                    root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String
                        ? errEl.GetString()
                        : "JS invocation failed");
            }

            w.WriteEndArray();
        }

        try
        {
            DotNetDispatcher.EndInvokeJS(runtime, Encoding.UTF8.GetString(stream.ToArray()));
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(RaskLogLevel.Error, "Rask.Native",
                $"Rask native jsResult dispatch for taskId={taskId} threw", ex);
        }
    }

    // JS-initiated [JSInvokable]: { type:"dotNetInvoke", callId, assemblyName, methodIdentifier,
    // dotNetObjectId?, argsJson }. Identical to the Server host's HandleDotNetInvoke.
    private static void HandleDotNetInvoke(NativeApp app, JsonElement root)
    {
        var assemblyName = root.TryGetProperty("assemblyName", out var aEl) && aEl.ValueKind == JsonValueKind.String
            ? aEl.GetString()
            : null;
        var methodIdentifier = root.TryGetProperty("methodIdentifier", out var mEl) && mEl.ValueKind == JsonValueKind.String
            ? mEl.GetString()
            : null;
        if (methodIdentifier is null)
        {
            return;
        }

        long dotNetObjectId = 0;
        if (root.TryGetProperty("dotNetObjectId", out var oEl) && oEl.ValueKind == JsonValueKind.Number)
        {
            oEl.TryGetInt64(out dotNetObjectId);
        }

        var callId = root.TryGetProperty("callId", out var cEl) && cEl.ValueKind == JsonValueKind.String
            ? cEl.GetString()
            : null;
        var argsJson = root.TryGetProperty("argsJson", out var argEl) && argEl.ValueKind == JsonValueKind.String
            ? argEl.GetString() ?? "[]"
            : "[]";

        var runtime = app.Services.GetService<NativeJSRuntime>();
        if (runtime is null)
        {
            return;
        }

        var invocationInfo = new DotNetInvocationInfo(assemblyName, methodIdentifier, dotNetObjectId, callId);
        try
        {
            DotNetDispatcher.BeginInvokeDotNet(runtime, invocationInfo, argsJson);
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(RaskLogLevel.Error, "Rask.Native",
                $"Rask native dotNetInvoke '{assemblyName}.{methodIdentifier}' threw", ex);
        }
    }

    // Client capability invoke: { type:"capability", component:"share", data:"<data-rask-share json>" }.
    // Routes to the registered device service so the native backend (the head's NativeShare) runs. Unknown
    // components no-op (forward-compatible: a newer client can request a capability an older host lacks).
    private static async Task HandleCapabilityAsync(NativeApp app, JsonElement root)
    {
        var component = root.TryGetProperty("component", out var cEl) && cEl.ValueKind == JsonValueKind.String
            ? cEl.GetString()
            : null;
        var dataJson = root.TryGetProperty("data", out var dEl) && dEl.ValueKind == JsonValueKind.String
            ? dEl.GetString()
            : null;

        if (component != "share" || string.IsNullOrEmpty(dataJson))
        {
            return;
        }

        ShareData? data;
        try
        {
            data = JsonSerializer.Deserialize(dataJson, RaskBrowserJsonContext.Default.ShareData);
        }
        catch (JsonException ex)
        {
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Native",
                "[Rask.Native] discarded a malformed share capability payload", ex);
            return;
        }

        if (data is null || app.Services.GetService<IShare>() is not { } share)
        {
            return;
        }

        try
        {
            await share.ShareAsync(data).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Native",
                "[Rask.Native] share capability invoke threw", ex);
        }
    }
}

/// <summary>
///     A running Native + Local app: the in-process live session and its service provider. Dispose to tear
///     down the component tree and the DI scope (e.g. from the platform head's stop/terminate lifecycle).
/// </summary>
public sealed class NativeApp : IAsyncDisposable
{
    internal NativeApp(NativeLiveSession session, ServiceProvider provider)
    {
        Session = session;
        _provider = provider;
    }

    internal NativeLiveSession Session { get; }
    private readonly ServiceProvider _provider;

    /// <summary>The app's service provider — resolve app services or the framework's browser APIs from it.</summary>
    public IServiceProvider Services => _provider;

    public async ValueTask DisposeAsync()
    {
        Session.Dispose();
        await _provider.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
///     A validated Native + Server target: the absolute URL of a remote Rask Server the platform head
///     loads into its WebView. The server serves its own client; native device APIs are available to the
///     loaded page. See <see cref="NativeAppHost.ConnectToServer" />.
/// </summary>
public sealed record NativeServerShell(Uri ServerBaseUrl);
