using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>One sync the browser woke the app up for.</summary>
/// <param name="Tag">The tag the app registered — how you tell your syncs apart.</param>
/// <param name="Periodic">
///     <see langword="false" /> for a one-shot connectivity sync (<c>SyncManager</c>),
///     <see langword="true" /> for a recurring one (<c>PeriodicSyncManager</c>).
/// </param>
public sealed record BackgroundSyncEvent(string Tag, bool Periodic);

/// <summary>
///     Typed access to
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Background_Synchronization_API">
///         Background Sync
///     </see>
///     and
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Web_Periodic_Background_Synchronization_API">
///         Periodic Background Sync
///     </see>
///     — ask the browser to wake your app when connectivity returns, or on a recurring schedule, so an edit
///     made offline can be flushed without the user going back to the tab and waiting.
///     <b>WASM-only:</b> both live on the service-worker registration, and a Server app renders over a live
///     WebSocket with no client-side runtime to wake into. Inject it through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         <b>Read this before you rely on it.</b> The browser will run the sync even with the tab closed —
///         but the .NET runtime lives in the page, not in the service worker, so <em>your C# only runs while
///         a client is open</em>. Rask's service worker forwards the woken-up tag to every open client; if
///         none is open the registration is consumed without your handler seeing it. Two consequences worth
///         designing around:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///                 <b>Re-request your tags at boot.</b> Treat a registration as best-effort, not as durable
///                 queue state. Keep the actual work queued somewhere you control — <c>IIndexedDb</c> or
///                 OPFS — and let the sync be the nudge to drain it, not the store.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>The realistic win is a backgrounded tab, not a closed one.</b> A tab that is merely
///                 hidden or frozen is still a client, so it wakes and drains the moment the network is back
///                 — which is the case most offline-first apps actually hit.
///             </description>
///         </item>
///     </list>
///     <para>
///         Needs a registered service worker (any Rask WASM PWA has one — see <c>docs/pwa.md</c>). Support is
///         Chromium-only at the time of writing; every call degrades to "unavailable" rather than throwing,
///         so a feature check is optional and a fallback is not.
///     </para>
/// </remarks>
public interface IBackgroundSync
{
    /// <summary>Whether the browser supports one-shot Background Sync (<c>SyncManager</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>Whether the browser supports Periodic Background Sync (<c>PeriodicSyncManager</c>).</summary>
    ValueTask<bool> IsPeriodicSupportedAsync();

    /// <summary>
    ///     Asks the browser to fire <paramref name="tag" /> once connectivity is available — immediately if
    ///     the app is already online. <see langword="false" /> when the API is unsupported, no service worker
    ///     is registered, or the browser refused; registering the same tag twice coalesces into one sync.
    /// </summary>
    /// <param name="tag">Your name for this sync, e.g. <c>"flush-drafts"</c>.</param>
    ValueTask<bool> RequestSyncAsync(string tag);

    /// <summary>Tags registered with <see cref="RequestSyncAsync" /> that have not fired yet. Empty when unsupported.</summary>
    ValueTask<IReadOnlyList<string>> GetPendingTagsAsync();

    /// <summary>
    ///     The state of the <c>periodic-background-sync</c> permission — <c>"granted"</c>, <c>"denied"</c>,
    ///     or <c>"prompt"</c>. Browsers grant it on their own terms (Chromium ties it to the app being
    ///     installed and to site engagement); there is no API to ask for it, so check rather than request.
    /// </summary>
    ValueTask<string> GetPeriodicPermissionAsync();

    /// <summary>
    ///     Registers a recurring sync for <paramref name="tag" />. <paramref name="minInterval" /> is a floor,
    ///     not a schedule: the browser decides the real cadence from engagement and battery, and in practice
    ///     fires far less often than you ask. <see langword="false" /> when unsupported or not permitted.
    /// </summary>
    /// <param name="tag">Your name for this sync.</param>
    /// <param name="minInterval">The shortest gap between firings. Must be positive.</param>
    ValueTask<bool> RequestPeriodicSyncAsync(string tag, TimeSpan minInterval);

    /// <summary>Removes a periodic registration. Harmless when the tag was never registered.</summary>
    ValueTask UnregisterPeriodicAsync(string tag);

    /// <summary>Tags currently registered for periodic sync. Empty when unsupported.</summary>
    ValueTask<IReadOnlyList<string>> GetPeriodicTagsAsync();

    /// <summary>
    ///     Subscribes to woken-up syncs, one-shot and periodic alike — check
    ///     <see cref="BackgroundSyncEvent.Periodic" /> to tell them apart. Dispose the handle to stop.
    ///     <para>
    ///         Subscribe from a lifecycle hook, before requesting a sync. A sync that arrived while the page
    ///         was still booting is delivered to the first subscriber rather than dropped, so an event that
    ///         beat your startup code still reaches it. A handler that changes state should call
    ///         <c>StateHasChanged()</c> — this is a subscription, not a render callback, so RASK026 doesn't
    ///         apply.
    ///     </para>
    /// </summary>
    ValueTask<IAsyncDisposable> OnSyncAsync(Func<BackgroundSyncEvent, Task> onSync);
}

/// <summary>
///     Infrastructure for <see cref="IBackgroundSync" /> — the entry point the service worker's forwarded
///     sync reaches. <b>Not for application use;</b> invoked only by the framework's <c>__raskSync</c> JS
///     helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BackgroundSyncInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<BackgroundSyncEvent, Task>> Handlers = new();

    internal static int Register(Func<BackgroundSyncEvent, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when a sync fires; do not call.</summary>
    /// <param name="periodic">Whether this came from <c>periodicsync</c> rather than <c>sync</c>.</param>
    /// <param name="tag">The registered tag the browser woke the app for.</param>
    [JSInvokable("RaskBackgroundSync")]
    public static Task Fired(bool periodic, string tag)
    {
        // One tag can legitimately interest several components (a draft queue and a badge count, say), so
        // every handler sees it — unlike the id-keyed device wrappers, where an event belongs to one watch.
        var reading = new BackgroundSyncEvent(tag, periodic);
        var handlers = Handlers.Values.ToArray();
        return handlers.Length == 0 ? Task.CompletedTask : Task.WhenAll(handlers.Select(h => h(reading)));
    }
}

/// <summary>
///     Default <see cref="IBackgroundSync" />, backed by the unified <see cref="IJSRuntime" /> and the
///     framework's WASM-only <c>__raskSync</c> helper.
/// </summary>
public sealed class BackgroundSync : IBackgroundSync
{
    private readonly IJSRuntime _js;

    // Root BackgroundSyncInterop's [JSInvokable] for the WASM trimmer — it is reached only through the JS
    // DotNetDispatcher (reflection), so without this the Fired method could be trimmed away.
    /// <summary>
    ///     Creates the service. Registered for you — inject <see cref="IBackgroundSync" /> rather than
    ///     constructing this.
    /// </summary>
    /// <param name="js">The JS interop runtime the wrapper calls through.</param>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(BackgroundSyncInterop))]
    public BackgroundSync(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskSync.supported");

    /// <inheritdoc />
    public ValueTask<bool> IsPeriodicSupportedAsync() => _js.InvokeAsync<bool>("__raskSync.periodicSupported");

    /// <inheritdoc />
    public ValueTask<bool> RequestSyncAsync(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return _js.InvokeAsync<bool>("__raskSync.request", tag);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> GetPendingTagsAsync() =>
        await _js.InvokeAsync<string[]>("__raskSync.tags").ConfigureAwait(false) ?? [];

    /// <inheritdoc />
    public ValueTask<string> GetPeriodicPermissionAsync() =>
        _js.InvokeAsync<string>("__raskSync.periodicPermission");

    /// <inheritdoc />
    public ValueTask<bool> RequestPeriodicSyncAsync(string tag, TimeSpan minInterval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minInterval, TimeSpan.Zero);
        return _js.InvokeAsync<bool>("__raskSync.requestPeriodic", tag, minInterval.TotalMilliseconds);
    }

    /// <inheritdoc />
    public ValueTask UnregisterPeriodicAsync(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return _js.InvokeVoidAsync("__raskSync.unregisterPeriodic", tag);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> GetPeriodicTagsAsync() =>
        await _js.InvokeAsync<string[]>("__raskSync.periodicTags").ConfigureAwait(false) ?? [];

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> OnSyncAsync(Func<BackgroundSyncEvent, Task> onSync)
    {
        ArgumentNullException.ThrowIfNull(onSync);

        var id = BackgroundSyncInterop.Register(onSync);
        try
        {
            // Idempotent on the JS side, and it is what releases anything the helper buffered during boot —
            // so the first subscriber sees a sync that landed before the runtime was ready.
            await _js.InvokeVoidAsync("__raskSync.listen").ConfigureAwait(false);
        }
        catch
        {
            BackgroundSyncInterop.Unregister(id);
            throw;
        }

        return new Subscription(id);
    }

    private sealed class Subscription(int id) : IAsyncDisposable
    {
        private bool _disposed;

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                BackgroundSyncInterop.Unregister(id);
            }

            // Nothing to tear down in JS: the helper keeps one message listener for the page's lifetime and
            // C# owns the fan-out, so unsubscribing is purely local.
            return ValueTask.CompletedTask;
        }
    }
}
