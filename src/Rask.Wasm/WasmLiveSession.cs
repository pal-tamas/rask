using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Authorization;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Wasm;

// Render/diff/payload pipeline + the IJSRuntime queue live in LiveSessionBase (Core), shared with
// the Server host. WasmLiveSession adds the in-process transport: the JSImport ApplyRender push,
// the single dispatch lock, the route-auth guard, the navigate/dispatch handlers, and the byte[]-
// per-frame model the JSExport boundary needs.
internal sealed class WasmLiveSession : LiveSessionBase
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    internal override SemaphoreSlim? DevToolsRenderGate => _lock;

    // Held so Dispose can unsubscribe symmetrically. The provider can outlive the session (it is a
    // separate service), so a dangling subscription would fire OnUserChanged on a disposed session
    // (disposed _lock → ObjectDisposedException). In the normal single-session-per-page lifetime
    // Dispose is never called, but keeping the teardown correct guards tests and any future
    // multi-session / re-init path.
    private readonly IUserProvider? _userProvider;

    // Host transport for LiveSessionBase.TryEmitFrameAsync: push the built frame to JS zero-copy via a
    // MemoryView over the write buffer. The JS applyRender reads the bytes synchronously (decode +
    // JSON.parse) within this call, so the view never outlives it — hence a synchronous, completed
    // ValueTask (no interop-boundary await on the browser's single thread).
    protected override ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame)
    {
        JSInterop.ApplyRender(MemoryMarshal.AsMemory(frame).Span);
        return default;
    }

    // Set by BuildPayloadAsync when the frame it built carries queued IJSRuntime calls. The
    // publish-render noop guard must NOT drop such a frame even when the HTML is unchanged — the
    // invokes still need to reach the client (where they run after applyDiff).
    private bool _lastBuildHadJsInvokes;

    // How long a frame that carries a navigation waits for the page it goes to. Zero sends at once.
    private readonly TimeSpan _navigationBudget;

    public WasmLiveSession(
        Component view, IServiceProvider services, LiveDiffMode diffMode, TimeSpan navigationBudget = default)
        : base(view, services, diffMode)
    {
        _navigationBudget = navigationBudget;

        // Bind this session to the runtime so its BeginInvokeJS queues onto JsInvokes.
        (services.GetService<WasmJSRuntime>())?.AttachHost(this);

        if (services.GetService<IUserProvider>() is { } userProvider)
        {
            _userProvider = userProvider;
            userProvider.Changed += OnUserChanged;
        }
    }

    // Browser WebAssembly runtime — in-process in the browser page (shell stays Web, platform None).
    protected override RenderEngine EngineCore => RenderEngine.Wasm;

    protected override void Dispose(bool disposing)
    {
        // Unsubscribe first so a late Changed can't fire OnUserChanged on the now-disposed _lock.
        if (_userProvider is not null)
        {
            _userProvider.Changed -= OnUserChanged;
        }

        // Same for the culture service, which is a root singleton here and outlives every session: left
        // subscribed, the next language switch would re-render this disposed tree against _lock (#1093).
        DetachCulture();

        ComponentLifecycle.DisposeComponentTree(View);
        // Single-threaded in the browser: no render can race this, so the arrays go straight back to the pool.
        _htmlBuffers.Dispose();
        _arrivals.Dispose();
        _repaints.Dispose();
        _lock.Dispose();
    }

    protected override async Task RenderInScopeCoreAsync()
    {
        // Mirror Rask.Server LiveSession.RenderAndSendAsync: when the framework asks for a
        // mid-await render (Component.InvokeWithRenderingAsync), build and push an intermediate
        // payload directly via JSInterop.ApplyRender so transient UI state — e.g. the async-
        // validator "Checking…" indicator that lives only during the validator's await window —
        // reaches the browser before the post-handler payload supersedes it. The dispatcher
        // already holds _lock and has InHandlerScope=true at this point, so no re-locking.
        //
        // Suppress the ambient SynchronizationContext (HandlerSyncContext, installed by
        // Component.InvokeWithRenderingAsync) for the duration of this call: BuildPayloadAsync's
        // internal `await Task.Yield()` would otherwise Post its continuation back through
        // HandlerSyncContext, which re-enters this same method via _render — a render loop on
        // the WASM JS task queue.
        // A handler that has navigated is not repainted mid-flight: the destination would be shown before the
        // dispatch has taken its address. The dispatch's render carries both.
        if ((_navigator ??= Services.GetRequiredService<Navigator>()).NavigationPending)
        {
            return;
        }

        var prevCtx = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            // One at a time. A handler's first repaint and the one before its continuation are asked for from
            // two places, and on a host with more than one thread — every test of this session — they walked
            // the tree together: one threw out of the walk, and with it went the handler's continuation or the
            // handler itself. The server session's render lock, for the renders a dispatch makes of itself.
            Interlocked.Increment(ref _repaintsWaiting);
            await _repaints.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            Interlocked.Decrement(ref _repaintsWaiting);
            try
            {
                await BuildPayloadAsync(null, false).ConfigureAwait(false);
                if (await TryEmitFrameAsync(false).ConfigureAwait(false))
                {
                    _htmlBuffers.Commit();
                }
            }
            finally
            {
                _repaints.Release();
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevCtx);
        }
    }

    // Serialises the repaints a handler asks for while it is still running (RenderInScopeCoreAsync).
    private readonly SemaphoreSlim _repaints = new(1, 1);
    private int _repaintsWaiting;

    // How many of a handler's repaints are waiting their turn — for the test that holds one mid-walk.
    internal int RepaintsWaiting => Volatile.Read(ref _repaintsWaiting);

    // Already inside a dispatch's render: signal it to rebuild the payload before releasing the lock, so
    // dispose-driven state changes (e.g. a token.Register callback that calls StateHasChanged on a parent)
    // land in the response payload instead of waiting for the next event.
    //
    // The scope is re-read after the write, as the server session does: a dispatch that finished its coalescing
    // loop and cleared the scope in between will not look at the flag again, so the request renders itself
    // instead of being dropped. A browser has one thread and never sees that; a host with more than one —
    // every test of this session — does.
    private bool LeftToTheDispatch()
    {
        if (!InHandlerScope)
        {
            return false;
        }

        _pendingRenderInScope = true;
        return InHandlerScope;
    }

    protected override async Task RequestRenderInternalAsync(bool publishOnly)
    {
        if (LeftToTheDispatch())
        {
            return;
        }

        // The app's services made ambient for exactly this work, as the server session does — how a Rask.Data
        // save inside it finds the IDataChanges that refreshes this page's queries.
        using var work = EnterWorkScope();
        await _lock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            await BuildPayloadCoalescingRerendersAsync(null, false, publishOnly)
                .ConfigureAwait(false);

            // A page that sent the reader on as it mounted here is not shown: its destination's frame follows.
            if ((_navigator ??= Services.GetRequiredService<Navigator>()).RedirectPending)
            {
                return;
            }

            // Noop publish-render guard: an auto-publish triggered by a completed
            // OnRendered that didn't mutate any tracked state produces the
            // same HTML. Sending it forces the JS side to morph identical HTML,
            // which strips any DOM state JS applied between the previous frame
            // and now — most visibly the `.hljs` class hljs added during the
            // previous frame's dispatch. Skip such frames entirely.
            if (publishOnly
                && !_lastBuildHadJsInvokes
                && _htmlBuffers.CurrentEqualsPrevious())
            {
                return;
            }

            // Push the built frame unless it's byte-identical to the last sent frame. EmitFrame's
            // double-buffered span compare (SIMD-accelerated) catches StateHasChanged calls that
            // didn't change visible output — no per-frame byte[].
            if (await TryEmitFrameAsync(false).ConfigureAwait(false))
            {
                _htmlBuffers.Commit();
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
            _ = DrainRenderRequestedAfterScope();
        }
    }

    private void OnUserChanged(object? sender, EventArgs e) => _ = RequestRender();

    // A navigation a lifecycle hook asked for with no dispatch waiting for it — after an await, or as its page
    // mounted in a render nothing dispatched. Made the way a link's is, replacing the address of the page that asked.
    protected override bool TryNavigateCore(Action navigate)
    {
        // The dispatch that mounted the asking page is still waiting for it: it makes the navigation itself,
        // in that page's place, so the page is never shown.
        if (_arriving)
        {
            _handedOver ??= navigate;
            return true;
        }

        _ = NavigateFromHookAsync(navigate);
        return true;
    }

    private async Task NavigateFromHookAsync(Action navigate)
    {
        // Never inline: the hook asking may be inside the very render this navigation has to wait for.
        await Task.Yield();
        using var work = EnterWorkScope();
        await _lock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            var navigator = Services.GetRequiredService<Navigator>();
            using var navigating = navigator.EnterHandler();
            navigate();
            if (!navigator.TryConsumeHistory(out var url, out _))
            {
                if (navigator.TryConsumeExit(out var exit))
                {
                    JSInterop.LeaveTo(LocalUrl.Sanitize(exit));
                }

                return;
            }

            if (await BuildPayloadCoalescingRerendersAsync(url, true, arriving: true).ConfigureAwait(false)
                && await TryEmitFrameAsync(true).ConfigureAwait(false))
            {
                _htmlBuffers.Commit();
            }
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Error, "Rask.Wasm", "Rask WASM navigation from a lifecycle hook threw", ex);
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
            _ = DrainRenderRequestedAfterScope();
        }
    }

    public async Task<byte[]> InitialRenderAsync()
    {
        using var work = EnterWorkScope();
        await _lock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            // Coalescing, not a bare BuildPayloadAsync: InHandlerScope is true for the whole of
            // this method, so any StateHasChanged raised while the first payload is being built
            // takes the short-circuit in RequestRenderInternalAsync and only sets
            // _pendingRenderInScope. A bare build never drains that flag, so the request was
            // dropped and the page kept its first-paint markup until some later event forced
            // another dispatch. The canonical loser is a Mount fetch whose continuation
            // lands after the walk materialised the HTML (OnRendered onwards) — the spinner then
            // stayed on screen forever. Every other render path already coalesces; this one was
            // the exception (#972).
            // The first page may send the reader on as it mounts; the address then changes in place.
            // And it is waited for like any page reached in place: what the browser shows meanwhile is the
            // document it loaded, which a baked page already fills.
            using var navigating = Services.GetRequiredService<Navigator>().EnterHandler();
            if (!await BuildPayloadCoalescingRerendersAsync(null, true, arriving: true).ConfigureAwait(false)
                || !await TryEmitFrameAsync(true).ConfigureAwait(false))
            {
                return Array.Empty<byte>();
            }

            _htmlBuffers.Commit();
            // The bootstrap caller wants the initial frame bytes; ToArray once per page load.
            return _lastSentBuffer!.WrittenSpan.ToArray();
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
            _ = DrainRenderRequestedAfterScope();
        }
    }

    private static readonly Task<byte[]> NoFrame = Task.FromResult(Array.Empty<byte>());

    // What a dispatch told not to copy returns after sending a frame: nobody reads it.
    private static readonly byte[] SentWithoutCopy = [];

    // The frame last pushed to JS, read in place — for tests of the no-copy path.
    internal ReadOnlySpan<byte> LastSentFrame => _lastSentBuffer is { } sent ? sent.WrittenSpan : default;

    // Push model: produce the render payload, then either return it to the caller
    // (tests) OR call JSInterop.ApplyRender from inside .NET (production). The JSExport
    // generator doesn't support Task<byte[]> as a return type — Task<string> works but
    // would force a base64 round-trip — so production callers use the push side.
    // Returning bytes preserves the test seam: tests can assert against the payload
    // without standing up a JS interop bridge.
    //
    // Not async itself: it only routes, and handing back the branch's own task keeps a frame to one
    // state machine.
    public Task<byte[]> DispatchAsync(byte[] json) => DispatchAsync(json, copyFrame: true);

    // copyFrame: hand the sent frame back as a copy — only the tests read it. Production (JSInterop.Dispatch)
    // passes false, since the frame already went to JS as a view over the write buffer and a copy per event
    // would be thrown away. The copy is taken under _lock: the drain after release may send over the buffer.
    internal Task<byte[]> DispatchAsync(byte[] json, bool copyFrame)
    {
        if (json is null || json.Length == 0)
        {
            return NoFrame;
        }

        var root = ParseFrame(json);

        var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;

        // Before the navigate branch, so a navigation counts as inbound traffic like any handler event.
        if (Rask.Core.Diagnostics.DevTools.RaskDevToolsHook.Active is { } devTools)
        {
            devTools.FrameReceived(this, json.Length, root);
        }

        if (string.Equals(type, "navigate", StringComparison.Ordinal))
        {
            return HandleNavigateAsync(root, copyFrame);
        }

        if (string.Equals(type, "batch", StringComparison.Ordinal))
        {
            return root.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array
                ? DispatchBatchAsync(events, copyFrame)
                : NoFrame;
        }

        var handlerId = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString()
            : null;
        return handlerId is null ? NoFrame : DispatchHandlerAsync(handlerId, root, copyFrame);
    }

    // Parse straight from the UTF-8 byte payload — no UTF-16 string materialisation.
    // JS hands the bytes across the interop boundary directly via TextEncoder.encode
    // on the send path, replacing the prior JSON.stringify + string-marshalled call.
    private static JsonElement ParseFrame(byte[] json)
    {
        using var doc = JsonDocument.Parse(json.AsMemory());
        return doc.RootElement.Clone();
    }

    private async Task<byte[]> DispatchHandlerAsync(string handlerId, JsonElement root, bool copyFrame)
    {
        using var work = EnterWorkScope();
        await _lock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            return await RunHandlerAsync(handlerId, root, copyFrame, deferRender: false).ConfigureAwait(false);
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
            _ = DrainRenderRequestedAfterScope();
        }
    }

    // The events one browser task produced, in one call: every handler runs in the order its event happened,
    // each seeing what the one before left, and the page is rendered ONCE after the last. The lock is held from
    // the first to that render, so no other dispatch runs between two of them — and the caller's promise, which
    // is what holds typed text back (rask-input.ts), resolves only when the render that answers them is applied.
    private async Task<byte[]> DispatchBatchAsync(JsonElement events, bool copyFrame)
    {
        // No more than the client ever sends are run: a longer batch did not come from it.
        var remaining = Math.Min(EventBatch.MaxEvents, EventBatch.CountEvents(events));
        if (remaining == 0)
        {
            return Array.Empty<byte>();
        }

        using var work = EnterWorkScope();
        await _lock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            var sent = Array.Empty<byte>();
            foreach (var e in events.EnumerateArray())
            {
                if (remaining == 0)
                {
                    break;
                }

                if (EventBatch.HandlerIdOf(e) is not { } handlerId)
                {
                    continue;
                }

                var last = --remaining == 0;

                // A render made now would rebuild this handler — an earlier event of the batch dirtied its
                // component or one above it — so the page is rendered first and the handler that runs is the one
                // that render registers, exactly as when each event was a call of its own.
                if (RenderOwed && !HandlerOutlivesRender(handlerId))
                {
                    sent = Latest(sent, await RenderOwedAsync(copyFrame).ConfigureAwait(false));
                }

                sent = Latest(sent, await RunHandlerAsync(handlerId, e, copyFrame, deferRender: !last).ConfigureAwait(false));

                // The last handler ran nothing, or threw: what the ones before it changed is still to be shown.
                if (last && RenderOwed)
                {
                    sent = Latest(sent, await RenderOwedAsync(copyFrame).ConfigureAwait(false));
                }
            }

            return sent;
        }
        finally
        {
            EndBatch();
            InHandlerScope = false;
            _lock.Release();
            _ = DrainRenderRequestedAfterScope();
        }
    }

    // The frame a batch answers with is the last one it sent; a step that sent none leaves the one before it.
    private static byte[] Latest(byte[] sent, byte[] frame) =>
        frame.Length > 0 || ReferenceEquals(frame, SentWithoutCopy) ? frame : sent;

    // The render a batch owes, made on its own: between two of its events, or after a last one that rendered
    // nothing. A fault in it is reported, and the batch goes on.
    private async Task<byte[]> RenderOwedAsync(bool copyFrame)
    {
        try
        {
            return await RenderAndEmitAsync(null, false, copyFrame).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(RaskLogLevel.Error, "Rask.Wasm", "Rask WASM batch render threw", ex);
            return Array.Empty<byte>();
        }
    }

    // One handler and the render that answers it, under the lock its caller holds. deferRender: more of a batch
    // follows, so a handler that sends the browser nowhere leaves its render to the one the batch makes later.
    private async Task<byte[]> RunHandlerAsync(string handlerId, JsonElement root, bool copyFrame, bool deferRender)
    {
        var navigator = Services.GetRequiredService<Navigator>();
        try
        {
            using (navigator.EnterHandler())
            {
                if (!await View.TryInvokeHandlerAsync(handlerId, root, Services, CancellationToken.None).ConfigureAwait(false))
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

                if (deferRender && historyUrl is null)
                {
                    OweRender();
                    return Array.Empty<byte>();
                }

                return await RenderAndEmitAsync(historyUrl, historyReplace, copyFrame).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Error,
                "Rask.Wasm",
                $"Rask WASM handler '{handlerId}' threw",
                ex);
            return Array.Empty<byte>();
        }
    }

    private async Task<byte[]> RenderAndEmitAsync(string? historyUrl, bool historyReplace, bool copyFrame)
    {
        // A render that carries a navigation waits for the page it goes to; one a newer navigation replaced
        // shows nothing.
        if (!await BuildPayloadCoalescingRerendersAsync(historyUrl, historyReplace, arriving: historyUrl is not null)
                .ConfigureAwait(false))
        {
            return Array.Empty<byte>();
        }

        // EmitFrame pushes the frame (zero-copy) unless it's byte-identical to the last
        // sent one; force the send when a navigation URL must flow even if the rendered
        // output is unchanged. No send → no-op (empty byte array to the test seam).
        if (!await TryEmitFrameAsync(historyUrl is not null).ConfigureAwait(false))
        {
            return Array.Empty<byte>();
        }

        _htmlBuffers.Commit();
        return copyFrame ? _lastSentBuffer!.WrittenSpan.ToArray() : SentWithoutCopy;
    }

    private async Task<byte[]> HandleNavigateAsync(JsonElement root, bool copyFrame)
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

        // Before the lock: this is what ends the wait of the navigation holding it.
        _arrivals.Read();
        return await NavigateAsync(navPath, navQueryString, replace, copyFrame).ConfigureAwait(false);
    }

    private async Task<byte[]> NavigateAsync(string navPath, string navQueryString, bool replace, bool copyFrame)
    {
        var fullUrl = navPath;
        if (!string.IsNullOrEmpty(navQueryString))
        {
            fullUrl += navQueryString.StartsWith('?') ? navQueryString : "?" + navQueryString;
        }

        using var work = EnterWorkScope();
        await _lock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            // A newer navigation is already waiting behind this one: the reader asked for that page since.
            if (!_arrivals.Starts())
            {
                return Array.Empty<byte>();
            }

            var routeState = Services.GetRequiredService<RouteState>();
            routeState.Path = navPath;
            routeState.Query = QueryString.Parse(navQueryString);

            try
            {
                // The page a navigation mounts may send the reader on, as it may from a handler.
                using var navigating = Services.GetRequiredService<Navigator>().EnterHandler();

                // Navigation always flows — force the send even when the rendered output is unchanged.
                if (!await BuildPayloadCoalescingRerendersAsync(fullUrl, replace, arriving: true).ConfigureAwait(false)
                    || !await TryEmitFrameAsync(true).ConfigureAwait(false))
                {
                    return Array.Empty<byte>();
                }

                _htmlBuffers.Commit();
                return copyFrame ? _lastSentBuffer!.WrittenSpan.ToArray() : SentWithoutCopy;
            }
            catch (Exception ex)
            {
                RaskDiagnostics.Report(
                    RaskLogLevel.Error,
                    "Rask.Wasm",
                    $"Rask WASM navigate '{navPath}' threw",
                    ex);
                return Array.Empty<byte>();
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
            _ = DrainRenderRequestedAfterScope();
        }
    }

    // arriving: the payload carries a navigation, so each build waits for the page it goes to (RenderArrivedAsync).
    // False when a newer navigation ended that wait: nothing was built, and nothing is to be sent.
    private async Task<bool> BuildPayloadCoalescingRerendersAsync(string? historyUrl,
        bool replace, bool publishOnly = false, bool arriving = false)
    {
        // First build emits any pending history navigation. If a dispose callback (or other
        // synchronous code reached during RenderAsLiveRoot) requested another render via
        // StateHasChanged, it set _pendingRenderInScope=true — rebuild so the dispatch's
        // returned payload carries the post-dispose state.
        //
        // historyUrl/replace flow through every rebuild: only the LAST result is returned
        // and sent, so re-passing the captured nav target is the only way for it to land
        // in the actually-emitted frame. Dropping them on the rebuild (as the previous
        // implementation did) silently swallowed handler-initiated navigation whenever a
        // publish-render rebuild fired — e.g. clicking the "Live ticker" sidebar entry
        // triggered LiveTicker.OnRendered, whose Chart.js-draw continuation requested
        // a publish render via `_pendingRenderInScope`; the rebuild then produced a
        // history-less payload and the URL stayed pinned to /index.html even though the
        // page itself routed correctly. There's no "duplicate pushState" risk because the
        // historyUrl here is a local captured before the call, not a fresh navigator
        // consumption — passing it twice still lands exactly one pushState on the client.
        // commitCache:false — every iteration diffs against the SAME (last-sent) baseline
        // rather than promoting its own render. Only the LAST build is sent, so committing each
        // intermediate rotation would make the final build diff against an un-sent render —
        // shipping a tiny stale diff (e.g. a head-only diff after a real page swap) that never
        // updates the body. We commit the final render once, after the loop. Each rebuild
        // overwrites _htmlBuffers.Current, so it holds the final render when the loop settles.
        _pendingRenderInScope = false;
        var budget = 2;
        FollowRedirect(ref historyUrl, ref replace, ref budget);
        if (!await BuildPayloadAsync(historyUrl, replace, publishOnly, false, arriving).ConfigureAwait(false))
        {
            _pendingRenderInScope = false;
            return false;
        }

        while (FollowRedirect(ref historyUrl, ref replace, ref budget) || (_pendingRenderInScope && budget-- > 0))
        {
            _pendingRenderInScope = false;
            if (!await BuildPayloadAsync(historyUrl, replace, publishOnly, false, arriving).ConfigureAwait(false))
            {
                _pendingRenderInScope = false;
                return false;
            }
        }

        // Commit the final, actually-sent render as the new diff baseline exactly once.
        _renderCache?.Snapshot();

        DropARenderPastTheBudget();
        return true;
    }

    // Budget exhaustion surfaces a third queued in-dispatch render that won't
    // flush — the next event picks up the trailing state. Match the server-side
    // telemetry so dispatch-render-loop bugs are greppable on either runtime.
    private void DropARenderPastTheBudget()
    {
        if (!_pendingRenderInScope)
        {
            return;
        }

        RaskDiagnostics.Report(
            RaskLogLevel.Warning,
            "Rask.Wasm",
            "[Rask.WasmLiveSession] Coalesce-loop budget exhausted; a third " +
            "in-dispatch render was queued and dropped. Inspect any handlers " +
            "that re-trigger StateHasChanged in OnRendered / dispose " +
            "callbacks during this dispatch.");

        // Consume it: reported and dropped ON PURPOSE. Leaving it set would hand it to
        // DrainRenderRequestedAfterScope, which would re-render, exhaust the budget again and
        // loop — the runaway the budget exists to stop.
        _pendingRenderInScope = false;
    }

    // A page that navigated as it mounted or updated: the payload just built is of a page the reader is not to
    // see, so the next build is of its destination and carries that address. The navigator stops a loop.
    private bool FollowRedirect(ref string? historyUrl, ref bool replace, ref int budget)
    {
        _navigator ??= Services.GetRequiredService<Navigator>();

        // Go.Out: the reader leaves for a page of this site the app does not render, and nothing follows.
        if (_navigator.TryConsumeExit(out var exit))
        {
            JSInterop.LeaveTo(LocalUrl.Sanitize(exit));
        }

        if (!_navigator.TryConsumeRedirect(out var url, out var redirectReplace))
        {
            return FollowHandedNavigation(ref historyUrl, ref replace, ref budget);
        }

        replace = replace || (historyUrl is null && redirectReplace);
        historyUrl = url;
        budget = 2;
        return true;
    }

    // A hook of the page being waited for navigated after an await: the dispatch makes that navigation in the
    // page's place. The page never had an address, so the one the reader came by is kept or replaced as their
    // own navigation said — and replaced when they made none.
    private bool FollowHandedNavigation(ref string? historyUrl, ref bool replace, ref int budget)
    {
        if (_handedOver is not { } navigate)
        {
            return false;
        }

        _handedOver = null;
        navigate();
        if (_navigator!.TryConsumeExit(out var exit))
        {
            JSInterop.LeaveTo(LocalUrl.Sanitize(exit));
        }

        if (!_navigator.TryConsumeHistory(out var url, out _))
        {
            return false;
        }

        replace = replace || historyUrl is null;
        historyUrl = url;
        budget = 2;
        return true;
    }

    private Navigator? _navigator;

    // The navigate calls made and not yet started, and the wait a newer one ends.
    private readonly NavigationArrivals _arrivals = new();

    // The browser has one thread, so plain fields: nothing here is read while something else writes it.
    private bool _arriving;
    private Action? _handedOver;

    private bool LeavingThePage => _navigator!.RedirectPending || _handedOver is not null;

    /// <summary>
    ///     Renders the page a navigation goes to, and renders it again as the lifecycle work that render
    ///     started comes back, until none is pending or the navigation budget is spent — the server host's
    ///     <c>RenderArrivedAsync</c>, over the same waves. Null when a newer navigation ended the wait.
    /// </summary>
    private async ValueTask<(ReadOnlyMemory<char> Html, FrameWriter? Frames)?> RenderArrivedAsync(bool publishOnly)
    {
        _navigator ??= Services.GetRequiredService<Navigator>();
        var superseded = _arrivals.Superseded;
        if (_arrivals.AnyQueued)
        {
            return null;
        }

        ReadOnlyMemory<char> html = default;
        FrameWriter? frameWriter = null;
        _arriving = true;
        _handedOver = null;
        try
        {
            await QuiescentRender.Run(
                again =>
                {
                    // A page that has sent the reader on is not rendered again: its destination is next.
                    if (!again || !LeavingThePage)
                    {
                        html = RenderTreeToHtml(publishOnly || again, out frameWriter);
                    }

                    return string.Empty;
                },
                _navigationBudget,
                // A call into the page's script completes only once a frame has carried it there.
                isBlocked: () => JsInvokes.HasPending || LeavingThePage,
                cancellationToken: superseded).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (superseded.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _arriving = false;
        }

        return (html, frameWriter);
    }

    // commitCache=false defers the render-cache rotation to the caller (the coalescing loop),
    // so intermediate rebuilds diff against the stable last-sent baseline instead of against
    // each other. Only the final, actually-sent build's render becomes the new baseline (the
    // loop calls Snapshot once after it settles). Direct senders (RenderInScopeAsync,
    // InitialRenderAsync) leave it true: their payload reaches the client, so it must commit.
    // Points the route at the guard's redirect when the reader may not see the page it is on, and answers the
    // address the browser should then show; null when the page is allowed.
    private async Task<string?> GuardRouteAsync()
    {
        var routeState = Services.GetRequiredService<RouteState>();
        if (!RouteResolver.TryResolve(routeState.Path, out var chain))
        {
            return null;
        }

        var user = Services.GetService<IUserProvider>()?.Current
                   ?? new ClaimsPrincipal(new ClaimsIdentity());
        var authResult = await RouteAuthorizationGuard
            .Evaluate(Services, chain, user)
            .ConfigureAwait(false);
        if (authResult.Outcome == RouteAuthorizationOutcome.Allow)
        {
            return null;
        }

        if (authResult.Outcome == RouteAuthorizationOutcome.Forbid)
        {
            routeState.Path = RouteAuthorizationGuard.ForbidPath;
            routeState.Query = QueryCollection.Empty;
            return RouteAuthorizationGuard.ForbidPath;
        }

        var returnUrl = "?returnUrl=" + Uri.EscapeDataString(QueryString.Build(routeState.Path, routeState.Query));
        routeState.Path = RouteAuthorizationGuard.ChallengePath;
        routeState.Query = QueryString.Parse(returnUrl);
        return RouteAuthorizationGuard.ChallengePath + returnUrl;
    }

    // False when the payload carried a navigation a newer one replaced while its page was awaited.
    internal async Task<bool> BuildPayloadAsync(string? historyUrl, bool replace,
        bool publishOnly = false, bool commitCache = true, bool arriving = false)
    {
        await Task.Yield();

        if (await GuardRouteAsync().ConfigureAwait(false) is { } guardUrl)
        {
            historyUrl = guardUrl;
            replace = true;
        }

        // Render + decide diff-vs-full + write the frame — shared with the Server host
        // (LiveSessionBase). WASM has no AuthInstruction in the diff codec (the route-auth guard
        // above already redirected), so auth is null; the data-rask-root id is the constant "wasm".
        ReadOnlyMemory<char> html;
        FrameWriter? frameWriter;
        if (arriving && _navigationBudget > TimeSpan.Zero)
        {
            if (await RenderArrivedAsync(publishOnly).ConfigureAwait(false) is not var (settled, frames))
            {
                return false;
            }

            (html, frameWriter) = (settled, frames);
        }
        else
        {
            html = RenderTreeToHtml(publishOnly, out frameWriter);
        }

        // A page that sent the reader on while it was awaited is not written: the caller builds its destination.
        if (arriving && _handedOver is not null)
        {
            _writeBuffer.ResetWrittenCount();
            return true;
        }

        var download = ConsumeDownload();

        // Drain IJSRuntime calls queued during the render walk (e.g. an OnRendered focus). They
        // ride this frame's jsInvokes and the client runs them AFTER applyDiff, so they act on the
        // committed DOM. _lastBuildHadJsInvokes lets the caller's noop guard ship this frame even
        // when the HTML is unchanged.
        var jsInvokes = JsInvokes.Drain();
        _lastBuildHadJsInvokes = jsInvokes is not null;

        WritePayload(html, frameWriter, download, jsInvokes, historyUrl, replace,
            commitCache, auth: null, sessionId: "wasm");

        // The built frame lives in _writeBuffer; EmitFrame pushes it zero-copy. The rendered chars stay
        // in _htmlBuffers.Current for the caller's noop-guard / diff-cache baseline (committed on send).
        return true;
    }
}
