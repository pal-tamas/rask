using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Authorization;
using Rask.Core.Components;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Native.Components;
using Rask.Native.Surface;

namespace Rask.Native;

// The render/diff/payload pipeline + the IJSRuntime queue live in LiveSessionBase (Core), shared with
// the Server and WASM hosts. NativeLiveSession adds the in-process native transport: it pushes each
// built frame to the platform WebView through INativeWebView.ApplyRenderAsync, holds a single dispatch
// lock, runs the route-auth guard inline (no server round-trip — like WASM), and turns WebView events
// into handler/navigate dispatches. Structurally a near-mirror of Rask.Wasm.WasmLiveSession; the only
// real difference is the transport (a WebView bridge instead of the WASM JSImport ApplyRender).
internal sealed class NativeLiveSession : LiveSessionBase, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Serializes the actual render+emit. Native runs async lifecycle/handler continuations on the thread
    // pool (HandlerSyncContext.Post uses Task.Run), so a mid-await render (RenderInScopeCoreAsync, or a
    // second continuation's render) can fire concurrently with the dispatch's render — and two renders
    // walking the component tree at once race ComponentLifecycle.DisposeComponentTree's PersistedChildren
    // enumeration ("Collection was modified; enumeration operation may not execute"), which trips the root
    // error boundary. Server has the same _renderLock; WASM is single-threaded so it needs none. It's held
    // only around one build+emit (never across a handler/await), so the legitimate re-entrant case —
    // InvokeWithRenderingAsync rendering inline inside a handler, then the dispatch's own render afterwards
    // — stays sequential (each acquires a free lock). Lock order is always _lock (if any) then _renderLock;
    // RenderInScopeCoreAsync takes only _renderLock, so there's no inversion.
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private readonly INativeWebView _webView;
    private readonly IUserProvider? _userProvider;

    // Set by BuildPayloadAsync when the frame it built carries queued IJSRuntime calls. The
    // publish-render noop guard must NOT drop such a frame even when the HTML is unchanged — the
    // invokes still need to reach the client (where they run after applyDiff).
    private bool _lastBuildHadJsInvokes;

    // Native header/footer chrome. Optional (null ⇒ feature off ⇒ fully backward compatible). _pendingHeader/
    // _pendingFooter are collected fresh each render walk (last bar of a kind in the tree wins); _lastPushedChrome
    // is the byte baseline for the noop guard (unchanged bars never re-push → no flicker on a counter tick);
    // _chromeTapHandlers maps a bar-button tap id to its OnClick, rebuilt every render so taps hit the latest.
    private readonly INativeChrome? _chrome;
    // Either a Rask.Native bar (NativeHeaderBar / NativeTabBar / NativeToolbar — platform-exact) or the
    // portable Rask.Core one (AppBar / TabStrip), which is what lets a single Screen subclass serve the web
    // and native heads. Typed as Component because the two families are deliberately unrelated: the
    // NativeComponent hierarchy is closed so this switch stays finite, and Rask.Core cannot name it.
    private Component? _pendingHeader;
    private Component? _pendingFooter;
    private byte[]? _lastPushedChrome;
    private Dictionary<string, Action> _chromeTapHandlers = new(StringComparer.Ordinal);

    // Pure-native content. Optional in exactly the same way as _chrome: with no INativeSurface registered the
    // NativeScreen family is inert and every frame paints through the WebView, so existing apps are untouched.
    //
    // _surfaceTree is the retained baseline the differ patches against. It, and the HTML baseline in
    // _lastSentBuffer, are BOTH truthful at all times because a surface backend only ever HIDES the content
    // view it is not showing — see INativeSurface. That is what makes switching between a web route and a
    // native route free: neither side re-mounts, and coming back to a web page does not reload it.
    private readonly INativeSurface? _surface;
    private readonly NativeTreeBuilder _treeBuilder = new();
    private NativeNode? _surfaceTree;
    private IReadOnlyDictionary<int, Func<string?, Task>> _surfaceHandlers =
        new Dictionary<int, Func<string?, Task>>();

    public NativeLiveSession(Component view, IServiceProvider services, INativeWebView webView, LiveDiffMode diffMode)
        : base(view, services, diffMode)
    {
        _webView = webView;
        _chrome = services.GetService<INativeChrome>();
        _surface = services.GetService<INativeSurface>();

        // Bind this session to the runtime so its BeginInvokeJS queues onto JsInvokes.
        services.GetService<NativeJSRuntime>()?.AttachHost(this, webView);

        // The base ctor already registered this session for hot-reload repaints; this only points the
        // "applied" indicator at the same WebView. No-op on a device build, where MetadataUpdater is
        // unsupported and no delta can arrive anyway (#565).
        HotReload.NativeHotReloadBridge.Attach(webView);

        if (services.GetService<IUserProvider>() is { } userProvider)
        {
            _userProvider = userProvider;
            userProvider.Changed += OnUserChanged;
        }
    }

    public void Dispose()
    {
        if (_userProvider is not null)
        {
            _userProvider.Changed -= OnUserChanged;
        }

        ComponentLifecycle.DisposeComponentTree(View);
        _lock.Dispose();
        _renderLock.Dispose();
    }

    // Native+Local: the app renders in-process inside the native app shell. Platform is read from the
    // runtime — the plain net10.0 build resolves both checks to false → None; the -ios/-android heads
    // resolve their OS. (Native+Server, where a native client drives a remote server, is a separate mode
    // whose shell/platform would arrive via a connection handshake — a tracked follow-up.)
    protected override RenderShell ShellCore => RenderShell.Native;
    protected override RenderEngine EngineCore => RenderEngine.InProcess;
    protected override RenderPlatform PlatformCore =>
        OperatingSystem.IsIOS() ? RenderPlatform.IOS
        : OperatingSystem.IsAndroid() ? RenderPlatform.Android
        : RenderPlatform.None;

    // ---- Native header/footer chrome ------------------------------------------------------------------------

    // Opt into the serializer's render-walk collection only when a backend is registered (else pure no-op).
    // Either backend needs it: the bars come from the same walk that the native view tree is rebuilt from.
    protected override bool CollectsNativeChromeCore => _chrome is not null || _surface is not null;

    // The serializer hands us every user component it walks; pick out the native bars composed in the tree —
    // a NativeHeaderBar becomes the header, a NativeTabBar/NativeToolbar the footer. Last of each kind wins
    // (the deepest layout in the walk). NativeWebView and bar items pass through here and are ignored.
    protected override void ReportNativeComponentCore(Component component)
    {
        switch (component)
        {
            case NativeHeaderBar or AppBar:
                _pendingHeader = component;
                break;
            case NativeTabBar or NativeToolbar or TabStrip:
                _pendingFooter = component;
                break;
        }

        // The same walk feeds the pure-native view tree. The builder ignores everything that is not a
        // NativeViewComponent, so the bars and the app's own components pass straight through it.
        if (_surface is not null)
        {
            _treeBuilder.Enter(component);
        }
    }

    // The closing half: a native component's node is materialized on its exit, once the children it collected
    // while open are known.
    protected override void ReportNativeComponentExitCore(Component component)
    {
        if (_surface is not null)
        {
            _treeBuilder.Exit(component);
        }
    }

    // Clear the last-collected chrome before each render walk so a removed bar drops out — the walk then
    // re-reports whatever the current tree composes (last of each kind wins).
    protected override void OnBeforeRenderWalk()
    {
        _pendingHeader = null;
        _pendingFooter = null;

        // Reset the collected view tree too, so a frame that renders no NativeScreen reports none — that is
        // exactly the signal that this frame's content is the WebView.
        _treeBuilder.Reset();
    }

    /// <summary>
    ///     Push everything this frame produced outside the WebView's HTML: the bars, then the native content.
    ///     Called under <c>_renderLock</c> right after a committed frame (same UI-thread + memory-validity
    ///     contract as <c>SendFrameAsync</c>), and a no-op when neither native backend is registered.
    /// </summary>
    private async Task PushNativeFrameAsync()
    {
        await PushChromeAsync().ConfigureAwait(false);
        await PushSurfaceAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Whether the frame just walked renders pure-native content rather than HTML. This is the content-mode
    ///     switch the whole mixed-surface design turns on: a native frame must not push its (empty) HTML to the
    ///     WebView, or the WebView's DOM would stop matching the HTML diff baseline and returning to a web
    ///     route would repaint it from scratch.
    /// </summary>
    private bool IsNativeFrame => _surface is not null && _treeBuilder.Root is not null;

    /// <summary>
    ///     The emit gate every send in this session goes through. On a native frame it reports "nothing sent"
    ///     WITHOUT touching the base's double-buffered baseline, so <c>_lastSentBuffer</c> keeps describing the
    ///     HTML the WebView is actually still showing.
    /// </summary>
    private ValueTask<bool> EmitFrameAsync(bool force) =>
        IsNativeFrame ? ValueTask.FromResult(false) : TryEmitFrameAsync(force);

    /// <summary>
    ///     Commit the frame just built — paint it, then push the bars and the native content — and return the
    ///     HTML frame bytes for the callers that report them as a test seam.
    /// </summary>
    /// <remarks>
    ///     A native frame paints through the surface and emits no HTML at all, so it must NOT take the
    ///     "nothing was emitted, bail out" path the HTML callers use: that would skip the surface push and the
    ///     screen would never update. Navigating from a web route to a native one goes through exactly here.
    /// </remarks>
    private async Task<byte[]> CommitFrameAsync(bool force)
    {
        if (IsNativeFrame)
        {
            await PushNativeFrameAsync().ConfigureAwait(false);
            return Array.Empty<byte>();
        }

        if (!await EmitFrameAsync(force).ConfigureAwait(false))
        {
            return Array.Empty<byte>();
        }

        _htmlBuffers.Commit();
        await PushNativeFrameAsync().ConfigureAwait(false);
        return _lastSentBuffer!.WrittenSpan.ToArray();
    }

    // Diff this frame's view tree against the retained one and push the result. A root whose kind changed
    // cannot be patched in place, so the differ says so and the whole tree re-mounts.
    private async Task PushSurfaceAsync()
    {
        if (_surface is null)
        {
            return;
        }

        // Adopt this frame's handler map even when the tree is unchanged: the delegates capture fresh state
        // every render, so a tap must always reach the latest closure.
        _surfaceHandlers = _treeBuilder.Handlers;

        if (_treeBuilder.Root is not { } root)
        {
            // No native content this frame — the page composed a NativeWebView (or nothing). Show the WebView
            // and KEEP the retained tree: the native view is only hidden, so returning to a native route
            // patches it instead of rebuilding it.
            await _surface.ShowWebViewAsync().ConfigureAwait(false);
            return;
        }

        if (_surfaceTree is { } previous && NativeTreeDiffer.Diff(previous, root) is { } patches)
        {
            _surfaceTree = root;
            await _surface.PatchAsync(patches).ConfigureAwait(false);
            return;
        }

        _surfaceTree = root;
        await _surface.MountAsync(root).ConfigureAwait(false);
    }

    // Build the descriptor from the just-collected header/footer, refresh the tap-handler map, and push to the
    // native bars only when the bytes changed. No-op when no INativeChrome is registered.
    private async Task PushChromeAsync()
    {
        if (_chrome is null)
        {
            return;
        }

        var currentPath = Services.GetRequiredService<RouteState>().Path;
        // Optional app-wide default appearance; a per-bar style prop overrides it, an unset slot keeps the default.
        var theme = Services.GetService<NativeTheme>();
        var handlers = new Dictionary<string, Action>(StringComparer.Ordinal);
        var descriptor = new NativeChromeDescriptor
        {
            Header = BuildHeaderDescriptor(_pendingHeader, handlers, theme),
            Footer = BuildFooterDescriptor(_pendingFooter, handlers, currentPath, theme),
        };
        // Refresh the tap-handler map every render — the OnClick delegates capture fresh state even when the
        // serialized descriptor is byte-identical, so a tap must always reach the latest closure.
        _chromeTapHandlers = handlers;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            descriptor, NativeChromeJsonContext.Default.NativeChromeDescriptor);
        if (_lastPushedChrome is not null && bytes.AsSpan().SequenceEqual(_lastPushedChrome))
        {
            return; // unchanged bars — no re-push, no flicker on a counter tick.
        }

        _lastPushedChrome = bytes;
        await _chrome.ApplyChromeAsync(bytes).ConfigureAwait(false);
    }

    // A per-bar style prop wins; the theme fills an unset slot; unset in both ⇒ null token ⇒ platform default.
    // An explicit NativeColor.System on a bar has a value (so it overrides the theme) but a null token, which
    // correctly forces the platform default for that slot.
    private static string? ResolveColor(NativeColor? barProp, NativeColor? themeProp) =>
        (barProp ?? themeProp)?.ToToken();

    private static NativeHeaderDescriptor? BuildHeaderDescriptor(
        Component? header, Dictionary<string, Action> handlers, NativeTheme? theme)
    {
        // The portable bar carries no appearance of its own — colour and segmented titles are
        // platform-exact features that stay with NativeHeaderBar — so it takes every style slot from the
        // app-wide NativeTheme.
        if (header is AppBar appBar)
        {
            var portable = new NativeHeaderDescriptor
            {
                Title = appBar.Title,
                Background = ResolveColor(null, theme?.Background),
                Tint = ResolveColor(null, theme?.Tint),
                TitleColor = ResolveColor(null, theme?.TitleColor),
            };
            if (appBar.Leading is { } portableLeading)
            {
                portable.Leading = BuildItemDescriptor(portableLeading, "h.leading", handlers);
            }

            if (appBar.Trailing is { Count: > 0 } portableTrailing)
            {
                portable.Trailing = new List<NativeBarItemDescriptor>(portableTrailing.Count);
                for (var i = 0; i < portableTrailing.Count; i++)
                {
                    portable.Trailing.Add(
                        BuildItemDescriptor(portableTrailing[i], "h.trailing." + i, handlers));
                }
            }

            return portable;
        }

        if (header is not NativeHeaderBar bar)
        {
            return null;
        }

        var dto = new NativeHeaderDescriptor
        {
            Title = bar.Title,
            Background = ResolveColor(bar.Background, theme?.Background),
            Tint = ResolveColor(bar.Tint, theme?.Tint),
            TitleColor = ResolveColor(bar.TitleColor, theme?.TitleColor),
        };
        if (bar.Leading is { } leading)
        {
            dto.Leading = BuildItemDescriptor(leading, "h.leading", handlers);
        }

        if (bar.Trailing is { Count: > 0 } trailing)
        {
            dto.Trailing = new List<NativeBarItemDescriptor>(trailing.Count);
            for (var i = 0; i < trailing.Count; i++)
            {
                dto.Trailing.Add(BuildItemDescriptor(trailing[i], "h.trailing." + i, handlers));
            }
        }

        if (bar.Segments is { Count: > 0 } segments)
        {
            dto.Segments = new List<NativeSegmentDescriptor>(segments.Count);
            for (var i = 0; i < segments.Count; i++)
            {
                string? id = null;
                if (bar.OnSegmentChanged is { } onChanged)
                {
                    var index = i; // capture per iteration so the tapped segment's index is echoed
                    id = "h.segment." + i;
                    handlers[id] = () => onChanged(index);
                }

                dto.Segments.Add(new NativeSegmentDescriptor { Title = segments[i], Id = id });
            }

            dto.SelectedSegment = Math.Clamp(bar.SelectedSegment ?? 0, 0, segments.Count - 1);
        }

        return dto;
    }

    private static NativeFooterDescriptor? BuildFooterDescriptor(
        Component? footer, Dictionary<string, Action> handlers, string currentPath, NativeTheme? theme)
    {
        switch (footer)
        {
            // The portable tab bar. Selection is derived by Rask.Core's own TabStrip.DeriveSelected — the
            // SAME method the web hosts call — so one declaration cannot light a different tab depending on
            // which head is running it.
            case TabStrip strip:
                var stripFooter = new NativeFooterDescriptor
                {
                    Kind = "tabbar",
                    Selected = strip.Selected ?? TabStrip.DeriveSelected(strip.Tabs, currentPath),
                    Background = ResolveColor(null, theme?.Background),
                    Tint = ResolveColor(null, theme?.Tint),
                    UnselectedTint = ResolveColor(null, theme?.UnselectedTint),
                };
                if (strip.Tabs is { Count: > 0 } stripTabs)
                {
                    stripFooter.Tabs = new List<NativeTabDescriptor>(stripTabs.Count);
                    foreach (var tab in stripTabs)
                    {
                        stripFooter.Tabs.Add(new NativeTabDescriptor
                        {
                            Title = tab.Title,
                            IosIcon = tab.Icon.IosSymbol,
                            AndroidIcon = tab.Icon.AndroidResource,
                            Path = tab.To.ToString(),
                            Badge = string.IsNullOrEmpty(tab.Badge) ? null : tab.Badge,
                        });
                    }
                }

                return stripFooter;

            case NativeTabBar tabBar:
                // Derive the active tab from the current route unless the page pinned Selected explicitly, so
                // the highlighted tab tracks navigation (a tap, hardware Back, or a deep link) automatically —
                // the caller never re-derives it by hand.
                var selected = tabBar.Selected ?? DeriveSelectedTab(tabBar.Tabs, currentPath);
                var tabFooter = new NativeFooterDescriptor
                {
                    Kind = "tabbar",
                    Selected = selected,
                    Background = ResolveColor(tabBar.Background, theme?.Background),
                    Tint = ResolveColor(tabBar.Tint, theme?.Tint),
                    UnselectedTint = ResolveColor(tabBar.UnselectedTint, theme?.UnselectedTint),
                };
                if (tabBar.Tabs is { Count: > 0 } tabs)
                {
                    tabFooter.Tabs = new List<NativeTabDescriptor>(tabs.Count);
                    foreach (var tab in tabs)
                    {
                        tabFooter.Tabs.Add(new NativeTabDescriptor
                        {
                            Title = tab.Title,
                            IosIcon = tab.Icon.IosSymbol,
                            AndroidIcon = tab.Icon.AndroidResource,
                            Path = tab.To.ToString(),
                            Badge = string.IsNullOrEmpty(tab.Badge) ? null : tab.Badge,
                        });
                    }
                }

                return tabFooter;

            case NativeToolbar toolbar:
                var toolFooter = new NativeFooterDescriptor
                {
                    Kind = "toolbar",
                    Background = ResolveColor(toolbar.Background, theme?.Background),
                    Tint = ResolveColor(toolbar.Tint, theme?.Tint),
                };
                if (toolbar.Items is { Count: > 0 } items)
                {
                    toolFooter.Items = new List<NativeBarItemDescriptor>(items.Count);
                    for (var i = 0; i < items.Count; i++)
                    {
                        toolFooter.Items.Add(BuildItemDescriptor(items[i], "f.item." + i, handlers));
                    }
                }

                return toolFooter;

            default:
                return null;
        }
    }

    private static NativeBarItemDescriptor BuildItemDescriptor(
        Component item, string id, Dictionary<string, Action> handlers)
    {
        switch (item)
        {
            // The portable bar button: an icon, a title, and an optional tap. Everything past that (a back
            // affordance, an overflow menu) stays with the Rask.Native family.
            case BarButton portable:
                string? portableTapId = null;
                if (portable.OnClick is { } portableClick)
                {
                    handlers[id] = portableClick;
                    portableTapId = id;
                }

                return new NativeBarItemDescriptor
                {
                    Kind = "button",
                    Id = portableTapId,
                    IosIcon = portable.Icon.IosSymbol,
                    AndroidIcon = portable.Icon.AndroidResource,
                    Title = portable.Title,
                };

            case NativeBackButton:
                // The head wires a back item to the platform's own back affordance — no server round-trip.
                return new NativeBarItemDescriptor { Kind = "back" };

            case NativeMenuButton menu:
                var menuIcon = menu.Icon ?? NativeIcon.More;
                var menuDto = new NativeBarItemDescriptor
                {
                    Kind = "menu",
                    IosIcon = menuIcon.IosSymbol,
                    AndroidIcon = menuIcon.AndroidResource,
                    Title = menu.Title ?? "More",
                };
                if (menu.Items is { Count: > 0 } entries)
                {
                    menuDto.Menu = new List<NativeMenuItemDescriptor>(entries.Count);
                    for (var i = 0; i < entries.Count; i++)
                    {
                        var entry = entries[i];
                        string? entryId = null;
                        if (entry.OnClick is { } entryClick)
                        {
                            entryId = id + ".menu." + i;
                            handlers[entryId] = entryClick;
                        }

                        menuDto.Menu.Add(new NativeMenuItemDescriptor
                        {
                            Title = entry.Title,
                            IosIcon = entry.Icon?.IosSymbol,
                            AndroidIcon = entry.Icon?.AndroidResource,
                            Id = entryId,
                            Destructive = entry.Destructive == true,
                        });
                    }
                }

                return menuDto;

            case NativeBarButton button:
                string? tapId = null;
                if (button.OnClick is { } onClick)
                {
                    handlers[id] = onClick;
                    tapId = id;
                }

                return new NativeBarItemDescriptor
                {
                    Kind = "button",
                    Id = tapId,
                    IosIcon = button.Icon.IosSymbol,
                    AndroidIcon = button.Icon.AndroidResource,
                    Title = button.Title,
                };

            default:
                return new NativeBarItemDescriptor { Kind = "button" };
        }
    }

    // The index of the tab whose route matches the current path (0 when none match), so the native tab bar
    // highlights the active page without the caller re-deriving Selected on every navigation.
    private static int DeriveSelectedTab(IReadOnlyList<NativeTab>? tabs, string currentPath)
    {
        if (tabs is null)
        {
            return 0;
        }

        for (var i = 0; i < tabs.Count; i++)
        {
            if (string.Equals(tabs[i].To.Path, currentPath, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    ///     Handle a native back affordance (<c>{"type":"back"}</c>, from a <c>NativeBackButton</c>): pop the
    ///     WebView's own history, exactly like the hardware Back button. The client's <c>popstate</c> listener
    ///     then sends a <c>navigate</c> to the now-current (previous) route, which re-enters the router — so back
    ///     reuses the existing history plumbing rather than a parallel server-side stack.
    /// </summary>
    public ValueTask GoBackAsync() => _webView.EvaluateJavaScriptAsync("window.history.back()");

    /// <summary>
    ///     Handle an interaction on a pure-native view — a button tap, a text field's edit, a switch toggle.
    ///     Resolves the handler id the surface echoed back against the map this session rebuilt on the last
    ///     render, awaits the delegate, then re-renders and pushes the resulting patches.
    /// </summary>
    /// <remarks>
    ///     The handler is AWAITED, so <c>OnClickAsync</c>/<c>OnInputAsync</c>/<c>OnChangedAsync</c> finish
    ///     before the frame is built and state they set after an <c>await</c> paints in that same frame rather
    ///     than a later one. It runs inside a <c>Navigator</c> handler scope like every other event path, so a
    ///     handler that navigates — including from a native screen to a WebView route — works and its history
    ///     push reaches the client.
    /// </remarks>
    /// <returns>The emitted HTML frame bytes, or empty when the frame painted natively (the test seam).</returns>
    public async Task<byte[]> DispatchSurfaceEventAsync(NativeSurfaceEvent surfaceEvent)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            // Resolve under the lock so a concurrent render's map swap can't race the lookup.
            if (!_surfaceHandlers.TryGetValue(surfaceEvent.HandlerId, out var handler))
            {
                return Array.Empty<byte>();
            }

            var navigator = Services.GetRequiredService<Navigator>();
            using (navigator.EnterHandler())
            {
                try
                {
                    await handler(surfaceEvent.Value).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RaskDiagnostics.Report(
                        RaskLogLevel.Error, "Rask.Native",
                        $"Rask native surface handler '{surfaceEvent.HandlerId}' threw", ex);
                    return Array.Empty<byte>();
                }

                string? historyUrl = null;
                var historyReplace = false;
                if (navigator.TryConsumeHistory(out var url, out var replace))
                {
                    historyUrl = url;
                    historyReplace = replace;
                }

                await _renderLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    await BuildPayloadCoalescingRerendersAsync(historyUrl, historyReplace).ConfigureAwait(false);
                    return await CommitFrameAsync(historyUrl is not null).ConfigureAwait(false);
                }
                finally
                {
                    _renderLock.Release();
                }
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    /// <summary>
    ///     Handle a bar-button tap (<c>{"type":"nativeTap","id":"…"}</c>): look up the button's <c>OnClick</c>,
    ///     invoke it (its factory wrapper re-renders the owner), then render + emit + push exactly like
    ///     <see cref="DispatchAsync" />. Returns the sent frame bytes (the test seam). A tab tap arrives as a
    ///     <c>navigate</c> message and flows through <see cref="HandleNavigateAsync" /> instead.
    /// </summary>
    public async Task<byte[]> DispatchNativeTapAsync(byte[] json)
    {
        if (json is null || json.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var doc = JsonDocument.Parse(json.AsMemory());
        var root = doc.RootElement;
        var id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString()
            : null;
        if (id is null)
        {
            return Array.Empty<byte>();
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            // Resolve the handler under the lock so a concurrent render's _chromeTapHandlers swap can't race the
            // lookup (read a stale closure or miss the id).
            if (!_chromeTapHandlers.TryGetValue(id, out var handler))
            {
                return Array.Empty<byte>();
            }

            // Run the tap inside a Navigator handler scope, exactly like a WebView handler event
            // (DispatchAsync) — a bar button that calls Navigator.NavigateTo must work, and its history push
            // must reach the client.
            var navigator = Services.GetRequiredService<Navigator>();
            using (navigator.EnterHandler())
            {
                try
                {
                    handler();
                }
                catch (Exception ex)
                {
                    RaskDiagnostics.Report(
                        RaskLogLevel.Error, "Rask.Native", $"Rask native bar tap '{id}' threw", ex);
                    return Array.Empty<byte>();
                }

                string? historyUrl = null;
                var historyReplace = false;
                if (navigator.TryConsumeHistory(out var url, out var replace))
                {
                    historyUrl = url;
                    historyReplace = replace;
                }

                await _renderLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    await BuildPayloadCoalescingRerendersAsync(historyUrl, historyReplace).ConfigureAwait(false);
                    var emitted = await EmitFrameAsync(historyUrl is not null).ConfigureAwait(false);
                    if (emitted)
                    {
                        _htmlBuffers.Commit();
                    }

                    // Push chrome even when the body produced no diff: a bar tap can change ONLY native chrome
                    // (a tab badge, a segmented selection, a menu action) and leave the HTML body identical, which
                    // emits no frame — but the bars still need the update.
                    await PushNativeFrameAsync().ConfigureAwait(false);
                    return emitted ? _lastSentBuffer!.WrittenSpan.ToArray() : Array.Empty<byte>();
                }
                finally
                {
                    _renderLock.Release();
                }
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    // Host transport for LiveSessionBase.TryEmitFrameAsync: hand the built frame to the platform WebView,
    // whose window.__raskNative.applyRender consumes it (applyDiff / morph). The memory is valid until the
    // returned ValueTask completes (the base awaits SendFrameAsync before swapping buffers), so a UI-thread
    // hop inside the platform implementation is safe.
    protected override ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame) => _webView.ApplyRenderAsync(frame);

    protected override async Task RenderInScopeCoreAsync()
    {
        // Mirror WasmLiveSession: when the framework asks for a mid-await render
        // (Component.InvokeWithRenderingAsync), build and push an intermediate payload directly so
        // transient UI state (e.g. an async-validator "Checking…" indicator) reaches the WebView before
        // the post-handler payload supersedes it. The dispatcher already holds _lock. Suppress the ambient
        // SynchronizationContext (HandlerSyncContext) for the duration so BuildPayloadAsync's internal
        // `await Task.Yield()` can't Post its continuation back through it and re-enter this method.
        var prevCtx = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        await _renderLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await BuildPayloadAsync(null, false).ConfigureAwait(false);
            if (await EmitFrameAsync(false).ConfigureAwait(false))
            {
                _htmlBuffers.Commit();
            }

            await PushNativeFrameAsync().ConfigureAwait(false);
        }
        finally
        {
            _renderLock.Release();
            SynchronizationContext.SetSynchronizationContext(prevCtx);
        }
    }

    protected override async Task RequestRenderInternalAsync(bool publishOnly)
    {
        if (InHandlerScope)
        {
            _pendingRenderInScope = true;
            return;
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        await _renderLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await BuildPayloadCoalescingRerendersAsync(null, false, publishOnly).ConfigureAwait(false);

            // Noop publish-render guard: an auto-publish from a completed OnRenderedAsync that didn't
            // mutate tracked state produces identical HTML; morphing it would strip DOM state JS applied
            // between frames. Skip such frames unless they carry queued IJSRuntime calls — but still push the
            // chrome, since native bars render no HTML and their change never shows in the HTML noop check.
            if (publishOnly && !_lastBuildHadJsInvokes && _htmlBuffers.CurrentEqualsPrevious())
            {
                await PushNativeFrameAsync().ConfigureAwait(false);
                return;
            }

            if (await EmitFrameAsync(false).ConfigureAwait(false))
            {
                _htmlBuffers.Commit();
            }

            await PushNativeFrameAsync().ConfigureAwait(false);
        }
        finally
        {
            _renderLock.Release();
            InHandlerScope = false;
            _lock.Release();
        }
    }

    private void OnUserChanged() => _ = RequestRenderAsync();

    /// <summary>
    ///     Build and push the first frame (a full-HTML morph onto <c>document.documentElement</c>). Called
    ///     once at boot from <see cref="NativeAppHost" />. Returns the sent bytes for diagnostics/tests.
    /// </summary>
    public async Task<byte[]> InitialRenderAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        await _renderLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Seed WebView history with the initial route as a REPLACE so it supersedes the boot shell URL
            // (/index.native.html) — otherwise Back from the first navigation (or hardware Back) lands on that
            // 404-ing shell path instead of the app's first screen.
            var initialPath = Services.GetRequiredService<RouteState>().Path;
            await BuildPayloadAsync(initialPath, replace: true).ConfigureAwait(false);
            return await CommitFrameAsync(true).ConfigureAwait(false);
        }
        finally
        {
            _renderLock.Release();
            InHandlerScope = false;
            _lock.Release();
        }
    }

    /// <summary>
    ///     Handle one WebView event message (a component <c>id</c>-carrying handler event, or a
    ///     <c>navigate</c>). Mirrors <c>WasmLiveSession.DispatchAsync</c>: parse the UTF-8 JSON, route,
    ///     invoke the handler, render, and push the frame. Returns the sent frame bytes (the test seam;
    ///     production also pushes them via <see cref="SendFrameAsync" />). <c>jsResult</c>/<c>dotNetInvoke</c>
    ///     messages are handled upstream by <see cref="NativeAppHost" /> before they reach here.
    /// </summary>
    public async Task<byte[]> DispatchAsync(byte[] json)
    {
        if (json is null || json.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var doc = JsonDocument.Parse(json.AsMemory());
        var root = doc.RootElement.Clone();

        var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;

        if (type == "navigate")
        {
            return await HandleNavigateAsync(root).ConfigureAwait(false);
        }

        var handlerId = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString()
            : null;
        if (handlerId is null)
        {
            return Array.Empty<byte>();
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            var navigator = Services.GetRequiredService<Navigator>();
            try
            {
                using (navigator.EnterHandler())
                {
                    if (!await View.TryInvokeHandlerAsync(handlerId, root, Services).ConfigureAwait(false))
                    {
                        return Array.Empty<byte>();
                    }

                    string? historyUrl = null;
                    var historyReplace = false;
                    if (navigator.TryConsumeHistory(out var url, out var replace))
                    {
                        historyUrl = url;
                        historyReplace = replace;
                    }

                    // Acquire _renderLock only around the render (not the handler above): an in-handler
                    // InvokeWithRenderingAsync renders inline under _renderLock first, so holding it across
                    // the handler would deadlock.
                    await _renderLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await BuildPayloadCoalescingRerendersAsync(historyUrl, historyReplace).ConfigureAwait(false);
                        return await CommitFrameAsync(historyUrl is not null).ConfigureAwait(false);
                    }
                    finally
                    {
                        _renderLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                RaskDiagnostics.Report(
                    RaskLogLevel.Error, "Rask.Native", $"Rask native handler '{handlerId}' threw", ex);
                return Array.Empty<byte>();
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    private async Task<byte[]> HandleNavigateAsync(JsonElement root)
    {
        var navPath = root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
        if (string.IsNullOrEmpty(navPath))
        {
            return Array.Empty<byte>();
        }

        var navQueryString = root.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String
            ? q.GetString() ?? string.Empty
            : string.Empty;
        var replace = root.TryGetProperty("replace", out var rEl) && rEl.ValueKind == JsonValueKind.True;

        var fullUrl = string.IsNullOrEmpty(navQueryString)
            ? navPath
            : navQueryString.StartsWith("?", StringComparison.Ordinal)
                ? navPath + navQueryString
                : navPath + "?" + navQueryString;

        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            var routeState = Services.GetRequiredService<RouteState>();
            routeState.Path = navPath;
            routeState.Query = QueryString.Parse(navQueryString);

            await _renderLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await BuildPayloadCoalescingRerendersAsync(fullUrl, replace).ConfigureAwait(false);
                return await CommitFrameAsync(true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RaskDiagnostics.Report(
                    RaskLogLevel.Error, "Rask.Native", $"Rask native navigate '{navPath}' threw", ex);
                return Array.Empty<byte>();
            }
            finally
            {
                _renderLock.Release();
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
        }
    }

    private async Task BuildPayloadCoalescingRerendersAsync(string? historyUrl, bool replace, bool publishOnly = false)
    {
        // Rebuild while in-dispatch StateHasChanged calls keep landing (dispose callbacks, an
        // OnRenderedAsync continuation) so the returned payload carries the settled state. Only the LAST
        // build is sent; commitCache:false keeps every iteration diffing against the stable last-sent
        // baseline, and the final render is committed once after the loop. See WasmLiveSession for the
        // full rationale (navigation-target re-pass, budget exhaustion telemetry).
        _pendingRenderInScope = false;
        await BuildPayloadAsync(historyUrl, replace, publishOnly, false).ConfigureAwait(false);
        var budget = 2;
        while (_pendingRenderInScope && budget-- > 0)
        {
            _pendingRenderInScope = false;
            await BuildPayloadAsync(historyUrl, replace, publishOnly, false).ConfigureAwait(false);
        }

        _renderCache?.Snapshot();

        if (_pendingRenderInScope)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Warning, "Rask.Native",
                "[Rask.NativeLiveSession] Coalesce-loop budget exhausted; a third in-dispatch render was " +
                "queued and dropped. Inspect handlers that re-trigger StateHasChanged in OnRenderedAsync / " +
                "dispose callbacks during this dispatch.");
        }
    }

    internal async Task BuildPayloadAsync(string? historyUrl, bool replace, bool publishOnly = false, bool commitCache = true)
    {
        await Task.Yield();

        var routeState = Services.GetRequiredService<RouteState>();
        if (RouteResolver.TryResolve(routeState.Path, out var chain))
        {
            var user = Services.GetService<IUserProvider>()?.Current
                       ?? new ClaimsPrincipal(new ClaimsIdentity());
            var authResult = await RouteAuthorizationGuard.EvaluateAsync(Services, chain, user).ConfigureAwait(false);
            if (authResult.Outcome != RouteAuthorizationOutcome.Allow)
            {
                var originalUrl = QueryString.Build(routeState.Path, routeState.Query);
                var redirectPath = authResult.Outcome == RouteAuthorizationOutcome.Forbid
                    ? RouteAuthorizationGuard.ForbidPath
                    : RouteAuthorizationGuard.ChallengePath;
                routeState.Path = redirectPath;
                if (authResult.Outcome == RouteAuthorizationOutcome.Challenge)
                {
                    routeState.Query = QueryString.Parse("?returnUrl=" + Uri.EscapeDataString(originalUrl));
                    historyUrl = redirectPath + "?returnUrl=" + Uri.EscapeDataString(originalUrl);
                }
                else
                {
                    routeState.Query = QueryCollection.Empty;
                    historyUrl = redirectPath;
                }

                replace = true;
            }
        }

        // Render + decide diff-vs-full + write the frame — shared with the Server/WASM hosts. Native has
        // no AuthInstruction in the diff codec (the route-auth guard above already redirected), so auth is
        // null; the data-rask-root id is the constant "native".
        var html = RenderTreeToHtml(publishOnly, out var frameWriter);
        var download = ConsumeDownload();

        var jsInvokes = JsInvokes.Drain();
        _lastBuildHadJsInvokes = jsInvokes is not null;

        WritePayload(html, frameWriter, download, jsInvokes, historyUrl, replace,
            commitCache, auth: null, sessionId: "native");
    }
}
