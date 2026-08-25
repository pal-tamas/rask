# IBackgroundSync

> Ask the browser to wake the app when connectivity returns, or on a recurring schedule.

- **Wraps:** Background Synchronization API + Web Periodic Background Synchronization API
- **MDN:** [SyncManager](https://developer.mozilla.org/en-US/docs/Web/API/Background_Synchronization_API) ·
  [PeriodicSyncManager](https://developer.mozilla.org/en-US/docs/Web/API/Web_Periodic_Background_Synchronization_API)
- **Home:** `Rask.Wasm.Browser` (WASM only)
- **Shape:** one-shot (register / list / unregister) + subscription (`OnSyncAsync` pushes to a callback)
- **Availability:** Web/Server ⬜ · PWA/WASM ✅

Both registrations live on the **service-worker registration**, and a Server app renders over a live
WebSocket with no client-side runtime to wake into — so this is WASM-only, and it needs a registered
service worker (every Rask WASM PWA has one; see the [PWA guide](../pwa.md)).

## Read this before you rely on it

The browser fires the sync **even with the tab closed**. Rask's guarantee is narrower, and the gap is
the part worth designing around: **the .NET runtime lives in the page, not in the service worker**, so
your C# only runs while a client is open. Rask's service worker forwards the woken-up tag to every
open client; with none open the registration is consumed without your handler seeing it.

Two consequences:

- **Re-request your tags at boot.** Treat a registration as best-effort rather than durable queue
  state. Keep the work itself in [`IIndexedDb`](indexeddb.md) or
  [OPFS](origin-private-file-system.md) and let the sync be the *nudge* to drain it, not the store.
- **The realistic win is a backgrounded tab, not a closed one.** A hidden or frozen tab is still a
  client, so it wakes and drains the moment the network is back — which is the case most offline-first
  apps actually hit.

Support is Chromium-only at the time of writing. Every call degrades to "unavailable" (`false`, or an
empty list) rather than throwing, so a feature check is optional and a fallback is not.

## Using it

```csharp
public sealed class DraftQueue(IBackgroundSync sync) : Component, IAsyncDisposable
{
    private IAsyncDisposable? _subscription;

    public override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender) return;

        // Subscribe BEFORE requesting: a sync that landed while the page was still booting is held for
        // the first subscriber, so an event that beat your startup code still reaches it.
        _subscription = await sync.OnSyncAsync(async e =>
        {
            if (e.Tag == "flush-drafts") await FlushAsync();
            StateHasChanged();
        });

        // Best-effort, and re-requested every boot — the browser may have consumed the last one while
        // no tab was open.
        await sync.RequestSyncAsync("flush-drafts");
    }

    public async ValueTask DisposeAsync() => await (_subscription?.DisposeAsync() ?? ValueTask.CompletedTask);
}
```

Periodic sync is gated on a permission the browser grants on its own terms (Chromium ties it to the app
being installed and to site engagement). There is no API to request it, so **check, don't ask**:

```csharp
if (await sync.IsPeriodicSupportedAsync() && await sync.GetPeriodicPermissionAsync() == "granted")
{
    // A floor, not a schedule — the browser decides the real cadence and in practice fires far less
    // often than you ask.
    await sync.RequestPeriodicSyncAsync("refresh-feed", TimeSpan.FromHours(12));
}
```

`OnSyncAsync` delivers both kinds; check `BackgroundSyncEvent.Periodic` to tell them apart. It is a
subscription handler, not a chain-set callback, so calling `StateHasChanged()` in it is correct and
[RASK026](../diagnostics.md) does not apply.

## See also

- Source: [`IBackgroundSync.cs`](../../src/Rask.Wasm/Browser/IBackgroundSync.cs)
- [Mobile & PWA guide](../pwa.md) — the service worker this rides on
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
