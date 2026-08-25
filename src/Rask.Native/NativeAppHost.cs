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
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Core.Messaging;
using Rask.Core.Routing;
using Rask.Native.Authentication;
using Rask.Native.Files;
using Rask.Native.Surface;

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

    // The platform module (iOS / Android) whose native backends take precedence over the JS fallbacks.
    // Applied in RunLocalAsync before the tier helpers so its registrations win (native-first).
    private INativePlatform? _platform;

    private NativeAppHost()
    {
        Services = new ServiceCollection();
        Services.AddLogging();
        Services.AddSingleton<RouteState>();
        Services.AddSingleton<Navigator>();
        // The declared state bag. Singleton for the same reason RouteState is (one session per app), and
        // registered here so a component shared with the server host resolves on both. Nothing carries it
        // anywhere on native — there is no server-side session to rebuild.
        Services.AddSingleton<PersistentState>();
        Services.AddSingleton<IPersistentState>(sp => sp.GetRequiredService<PersistentState>());
        // Singleton = one queue for the app instance (the whole native app is a single session), so a
        // message queued before a NavigateTo survives it. Same model as WasmHostBuilder.
        Services.AddSingleton<IToaster, Toaster>();

        // The transport-agnostic browser-API surface (Rask.Core.Browser + the in-process IShare) is NOT wired
        // here — it is registered in RunLocalAsync, AFTER any INativePlatform passed to UsePlatform has added
        // its native C# backends. Because the tier helpers use TryAdd, a native backend registered by the
        // platform (or an explicit app registration) wins, and every interface it does not cover falls back to
        // the WebView's JS engine. This is the framework-picks-the-best-impl path — see UsePlatform below.

        Services.TryAddSingleton<IUserProvider, AnonymousUserProvider>();
        // The rest of Rask.Core's per-host contract (RaskHostContracts.HostServices). These three were the
        // gap this host shipped with: a component shared with the web heads lost its file uploads silently,
        // threw on Navigator.Download, and failed DI outright on IAuthSignIn — all only on native, all only
        // at runtime. TryAdd throughout, so an app or platform module that registers its own still wins.
        Services.TryAddSingleton<IBrowserFileBackend, NativeFileBackend>();
        Services.TryAddSingleton<NativeDownloadSink>();
        Services.TryAddSingleton<IDownloadSink>(sp => sp.GetRequiredService<NativeDownloadSink>());
        // No platform head registered one, so downloads land on disk and say so rather than vanishing.
        Services.TryAddSingleton<INativeFileExport, DiagnosticFileExport>();
        Services.TryAddSingleton<IAuthSignIn, NativeAuthSignIn>();
        Services.AddAuthorizationCore();

        // IJSRuntime backed by the native WebView bridge. Singleton — one runtime per app instance;
        // NativeLiveSession's ctor binds it to the session + WebView.
        // Live capability subscriptions (a GPS watch, a held wake lock, the sensor streams). Singleton
        // because the whole native app is one session, and disposed with the app so nothing it started
        // outlives it.
        Services.TryAddSingleton<NativeCapabilitySubscriptions>();

        Services.AddSingleton<NativeJSRuntime>();
        Services.AddSingleton<IJSRuntime>(sp => sp.GetRequiredService<NativeJSRuntime>());
    }

    /// <summary>The DI container for the app. Register your services here before running.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    ///     Installs a native platform module (e.g. <c>ApplePlatform</c> / <c>AndroidPlatform</c> from the
    ///     platform head) whose native C# backends implement the browser/device API interfaces. The host
    ///     applies it in <see cref="RunLocalAsync{TApp}" /> before the JS-backed fallbacks, so any interface
    ///     the platform backs natively wins and the rest fall back to the WebView. Returns <c>this</c> for
    ///     chaining. Calling it again replaces the previous module.
    /// </summary>
    public NativeAppHost UsePlatform(INativePlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        _platform = platform;
        return this;
    }

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
    /// <typeparam name="TApp">
    ///     The root <see cref="Component" />. It renders into <c>&lt;body&gt;</c>; Rask composes the
    ///     document around it (RASK021 flags a root that builds the shell itself).
    /// </typeparam>
    public Task<NativeApp> RunLocalAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        INativeWebView webView, string initialPath = "/")
        where TApp : Component
    {
        ArgumentNullException.ThrowIfNull(webView);
        return RunAsync<TApp>(webView, initialPath);
    }

    /// <summary>
    ///     <b>Native + Pure-native</b> — boots the app with <b>no WebView at all</b>. The component tree
    ///     paints as real platform views through <paramref name="surface" />, and nothing HTML is
    ///     instantiated.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The counterpart to <see cref="RunLocalAsync{TApp}" />, which drives a WebView and can mix
    ///         native screens into it. Use this when the app has no HTML at all: there is no WebView to
    ///         create, so nothing pays for one, and the first frame is a view tree rather than a document.
    ///     </para>
    ///     <para>
    ///         Two things behave differently and are not hidden. <c>IJSRuntime</c> has no engine to dispatch
    ///         into and says so if called, and rendering HTML raises a named error from
    ///         <c>NativeLiveSession.SendFrameAsync</c> rather than failing silently — see #777. Back
    ///         navigation moves to the session's own history, so Android's hardware Back button works with
    ///         no page history to pop.
    ///     </para>
    ///     <para>
    ///         There is also no <c>ready</c> handshake: a WebView client posts that once its document has
    ///         loaded, and with no document this host performs the first render itself before returning, so
    ///         the caller gets an app that is already on screen.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TApp">The root component.</typeparam>
    /// <param name="surface">The platform surface backend that paints the view tree.</param>
    /// <param name="initialPath">The route to boot on.</param>
    public async Task<NativeApp> RunNativeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        INativeSurface surface, string initialPath = "/")
        where TApp : Component
    {
        ArgumentNullException.ThrowIfNull(surface);

        // Registered before the provider is built so the session resolves the same instance the caller
        // owns, exactly as a platform head's UsePlatform registration would.
        Services.AddSingleton(surface);

        var app = await RunAsync<TApp>(webView: null, initialPath).ConfigureAwait(false);

        // No client to post `ready`, so the first frame is this host's job.
        await app.Session.InitialRenderAsync().ConfigureAwait(false);

        // …and if that frame turned out to be HTML, say so HERE rather than from inside the render. The
        // session drops such a frame and records it, because throwing mid-render reaches the root error
        // boundary, whose answer is to render an error page — more HTML, dropped again, for ever. By this
        // point the render has unwound, so the error is just an error.
        if (app.Session.RenderedHtmlWithNoWebView)
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(NativeLiveSession.HtmlWithoutWebViewMessage);
        }

        return app;
    }

    private async Task<NativeApp> RunAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        INativeWebView? webView, string initialPath)
        where TApp : Component
    {

        // Native-first wiring: the platform's native backends first, then the JS-backed fallbacks via TryAdd,
        // so a natively-backed interface wins and everything else resolves to the WebView's JS engine. The
        // WASM-only tier is intentionally absent — Rask.Native does not reference Rask.Wasm.
        _platform?.Register(Services);
        Services.AddCoreBrowserApis(ServiceLifetime.Singleton);
        Services.AddClientBrowserApis(ServiceLifetime.Singleton);

        var provider = Services.BuildServiceProvider();

        var app = ActivatorUtilities.CreateInstance<TApp>(provider);
        // Host-bootstrap construction of the framework's root boundary, identical to Rask.Server /
        // Rask.Wasm — there is no LiveRenderContext yet, so the generated factory (which needs one) can't be
        // used here. RASK014 only fires because Rask.Native now runs the analyzer over its own sources to emit
        // the Native* chrome factories; the Server/Wasm hosts do the same `new` but don't run the analyzer.
#pragma warning disable RASK014 // intentional framework host-bootstrap construction
        var root = new RootErrorBoundary(app);
#pragma warning restore RASK014

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
        // Derived from the module, not declared: adding a sixteenth backend advertises it, and a head with
        // no module advertises nothing rather than promising backends it does not have.
        nativeApp.Capabilities = _platform is null
            ? []
            : NativeCapabilityRegistry.AdvertisedFor(_platform);

        // The WebView drives everything through this single message channel: the initial-render handshake
        // (ready), IJSRuntime results (jsResult), JS-initiated [JSInvokable] (dotNetInvoke), and component
        // events (everything else → the session). Mirrors how the WASM host multiplexes its JSExports.
        // Absent in the pure-native model, where the surface's own event channel below is the only input.
        if (webView is not null)
        {
            webView.OnMessage = json => RouteMessageAsync(
                nativeApp, json, script => webView.EvaluateJavaScriptAsync(script));

            // Derived from the module, not declared: adding a sixteenth backend advertises it, and a head
            // with no module advertises nothing rather than promising backends it does not have.
            webView.Capabilities = nativeApp.Capabilities;
        }

        // If a native-chrome backend is registered, route its bar interactions through the SAME dispatcher —
        // a button tap ({type:"nativeTap"}) and a tab tap ({type:"navigate"}) re-enter the router exactly like
        // WebView events, so there is no separate native-input path.
        if (provider.GetService<INativeChrome>() is { } chrome)
        {
            chrome.OnChromeEvent = json => RouteMessageAsync(nativeApp, json);
        }

        // Same idea for a pure-native surface, but its events carry a handler id rather than a JSON message,
        // so they go straight to the session's typed entry point instead of through the JSON router.
        if (provider.GetService<INativeSurface>() is { } surface)
        {
            surface.OnSurfaceEvent = e => nativeApp.Session.DispatchSurfaceEventAsync(e);
        }

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

    /// <param name="app">The running app whose session and services the message targets.</param>
    /// <param name="json">The raw UTF-8 message.</param>
    /// <param name="evaluate">
    ///     Evaluates JS in the WebView that sent the message — the capability bridge's reply channel.
    ///     Null for input that did not come from a WebView (a native bar tap), which cannot carry a
    ///     capability envelope and so never needs to answer one.
    /// </param>
    private static async Task RouteMessageAsync(
        NativeApp app, byte[] json, Func<string, ValueTask>? evaluate = null)
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
                // Tell the page what this head backs natively, BEFORE the first frame: a component that
                // reaches for a device API while rendering must already know whether to cross the bridge.
                // The in-process client ships with an empty list because what is native is a property of
                // the platform module the head was given, not of the client.
                if (evaluate is not null)
                {
                    await evaluate(NativeCapabilities.AdvertiseScript(app.Capabilities)).ConfigureAwait(false);
                }

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
                // A client capability invoke (window.__raskNative.invoke). Route it through the shared
                // NativeCapabilities dispatcher with the DI-registered service, so a declarative Shareable
                // reaches the native backend the head registered — see docs/native.md. (A Native + Server
                // head uses the same dispatcher with its own IShare.)
                // The reply channel is the WebView this session already drives. Handed in rather than
                // reached for, so the remote heads — which have a WebView but no session — use the same
                // dispatcher with their own.
                if (evaluate is not null)
                {
                    await NativeCapabilities.TryHandleAsync(json, app.Services, evaluate).ConfigureAwait(false);
                }

                return;
            case "chrome":
                // Bars described by a REMOTE session. In URL mode this head's WebView is showing an app that
                // runs elsewhere — on a server, or as a hosted WASM bundle — and that app's Screen declared
                // the bars. It cannot draw them: only this process can touch a UINavigationBar or a
                // BottomNavigationView. So it sends the descriptor, and the head applies it through the same
                // INativeChrome the in-process session uses. One chrome path, two sources.
                await NativeShellChrome
                    .TryApplyAsync(json, app.Services.GetService<INativeChrome>())
                    .ConfigureAwait(false);
                return;
            case "nativeTap":
                // A native bar-button tap — invoke its OnClick and re-render (tabs arrive as "navigate" and
                // flow through DispatchAsync's default path below).
                //
                // The bar may belong to a remote session instead, in which case the OnClick does not exist in
                // this process at all. Hand it back to the page, which forwards it over its own transport to
                // the session that does own it. Checked before dispatching rather than after: an unmatched id
                // and a handler that threw both come back as an empty frame, and forwarding a tap that ALREADY
                // ran would run it twice.
                if (!app.Session.OwnsChromeTap(root) && evaluate is not null
                    && NativeShellChrome.TapScriptFor(json) is { } forward)
                {
                    await evaluate(forward).ConfigureAwait(false);
                    return;
                }

                await app.Session.DispatchNativeTapAsync(json).ConfigureAwait(false);
                return;
            case "back":
                // A native back button — pop WebView history (its popstate re-enters the router as a navigate).
                await app.Session.GoBackAsync().ConfigureAwait(false);
                return;
            case "download":
                // Navigator.Download staged bytes and the client handed the token back. Pull them, write the
                // file, and let the platform present it. See HandleDownloadAsync.
                await HandleDownloadAsync(app, root).ConfigureAwait(false);
                return;
            default:
                await app.Session.DispatchAsync(json).ConfigureAwait(false);
                return;
        }
    }

    // { type:"download", token:"<hex>" } — the client echoing back the token Navigator.Download put in the
    // render payload. The bytes never left .NET; this drains them, stages a file under the app's cache
    // directory, and hands it to INativeFileExport (the OS share sheet on a real platform head).
    //
    // Every failure here is reported rather than thrown: this runs on the WebView's message pump, where an
    // escaping exception takes down message routing for the whole app — a failed download must not cost the
    // user their session.
    private static async Task HandleDownloadAsync(NativeApp app, JsonElement root)
    {
        if (!root.TryGetProperty("token", out var tokenEl) || tokenEl.ValueKind != JsonValueKind.String
            || tokenEl.GetString() is not { Length: > 0 } token)
        {
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Native",
                "[Rask.Native] discarded a download message with no token");
            return;
        }

        // A stale or replayed token pulls nothing — the client is the one place a token can arrive twice.
        if (app.Services.GetService<IDownloadSink>() is not NativeDownloadSink sink
            || sink.Pull(token) is not { } staged)
        {
            return;
        }

        try
        {
            var file = await NativeDownloadStaging
                .StageAsync(staged.FileName, staged.ContentType, staged.Bytes).ConfigureAwait(false);
            var export = app.Services.GetService<INativeFileExport>() ?? new DiagnosticFileExport();
            await export.ExportAsync(file).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(RaskLogLevel.Error, "Rask.Native",
                $"[Rask.Native] failed to hand the download '{staged.FileName}' to the platform", ex);
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

    /// <summary>
    ///     What this app backs natively, derived from the platform module it was given. Empty when there is
    ///     none, which is what makes a head with no module degrade to the WebView's own web APIs.
    /// </summary>
    internal IReadOnlyList<string> Capabilities { get; set; } = [];

    /// <summary>
    ///     Whether <see cref="GoBackAsync" /> has somewhere to go. Answered synchronously, because the caller
    ///     that needs it — Android's <c>OnBackPressed</c> — has to decide in the moment whether to handle the
    ///     press or let it close the activity:
    ///     <code>
    ///     public override void OnBackPressed()
    ///     {
    ///         if (_app is { CanGoBack: true } app) { _ = app.GoBackAsync(); return; }
    ///         base.OnBackPressed();   // nothing to pop — let Back close the app
    ///     }
    ///     </code>
    ///     False for an app with a WebView: the page owns that history and reading it needs a round trip,
    ///     which a synchronous answer cannot afford. Such a head keeps the platform's default Back behaviour.
    /// </summary>
    public bool CanGoBack => Session.CanGoBack;

    /// <summary>
    ///     Navigate back one entry, if there is one. The counterpart of the platform's own back affordance,
    ///     for a head that wants to route the hardware button through the app's history rather than the
    ///     activity stack. A no-op when <see cref="CanGoBack" /> is false.
    /// </summary>
    public ValueTask GoBackAsync() => Session.GoBackAsync();

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
