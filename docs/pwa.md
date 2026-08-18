# 📱 Mobile apps with Rask — PWA, offline & push

**Build installable, offline, native-feeling mobile apps in C# — no Swift, Kotlin, React Native, or
MAUI.** A Rask **WASM** app is a Progressive Web App: it installs to the home screen, launches
full-screen, works offline, sends push notifications, and reaches device capabilities (vibration,
share sheet, geolocation, clipboard) through typed C# — the same component code you already write.

> **WASM vs Server.** A WASM app gets the *full* PWA: install, **true offline**, push, and every device
> API. A **Server** app (opt in with `AddRaskPwa`) is **installable + push-capable** — manifest, Web Push
> subscribe, local notifications, app badge, and wake lock all work — but it is **not an offline app**:
> it renders over a live WebSocket, so offline navigations show a static offline page, and there is no
> install-prompt replay, and no **background sync** (both stay WASM-only). See
> [choosing a host template](getting-started.md#1-scaffold-a-project) and
> [PWA on the Server host](#pwa-on-the-server-host) below.

- [Make your app a PWA](#make-your-app-a-pwa)
- [Installable — the web app manifest](#installable--the-web-app-manifest)
- [Custom install button (`IInstallPrompt`)](#custom-install-button-iinstallprompt)
- [Offline — the service worker](#offline--the-service-worker)
- [Background sync (`IBackgroundSync`)](#background-sync-ibackgroundsync)
- [Push notifications (`IWebPush`)](#push-notifications-iwebpush)
- [PWA on the Server host](#pwa-on-the-server-host)
- [Device capabilities for mobile](#device-capabilities-for-mobile)
- [Deploying (GitHub Pages & sub-paths)](#deploying-github-pages--sub-paths)

---

## Make your app a PWA

Start a new app with the **`--pwa`** option:

```bash
rask new MyApp --template wasm --pwa          # standalone browser-WASM PWA (full offline)
rask new MyApp --template wasm-hosted --pwa   # WASM PWA + ASP.NET host (full offline)
rask new MyApp --pwa                          # installable + push-capable Server app (not offline)
```

The WASM templates scaffold a manifest + icon and register Rask's default service worker from
`index.html`. The Server template calls `AddRaskPwa(...)`, which serves the manifest + service worker
and registers it for you, plus a static `offline.html`. To add PWA to an existing app, follow the steps
below — the [manifest](#installable--the-web-app-manifest) and, for WASM,
[service-worker registration](#offline--the-service-worker); for Server, just
[`AddRaskPwa`](#pwa-on-the-server-host).

---

## Installable — the web app manifest

Configure a typed `WebAppManifest` (in `Rask.Core.Browser`) in `Program.cs` — on WASM the framework
injects the `<link rel="manifest">` (a `data:` URL, so **no `manifest.webmanifest` file to ship**) and
the `<meta name="theme-color">` at boot; on Server `AddRaskPwa` serves and links it. There's nothing to
hand-write or keep in sync:

```csharp
using Rask.Core.Browser;

var host = WasmHostBuilder.CreateDefault();
host.UsePwa(new WebAppManifest
{
    Name = "My Rask App",
    ShortName = "Rask App",
    ThemeColor = "#512BD4",
    BackgroundColor = "#faf9fe",
    Display = DisplayMode.Standalone,
    Icons = [new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable")]
});
await host.RunAsync<App>();
```

Relative URLs (`StartUrl`/`Scope` default to `"."`, and icon `src`) are made **absolute against
`<base href>`** when applied, so they stay correct under a sub-path deploy (GitHub Pages). Put your
icon(s) in `wwwroot` (the `--pwa` templates ship an `icon.svg`). `WebAppManifest.ToJson()` is also
available if you'd rather serve a physical `manifest.webmanifest` (e.g. from an ASP.NET host).

Beyond the basics, `WebAppManifest` also exposes typed members for the richer manifest features — all
optional and omitted when unset:

| Member | Manifest key | Use |
| --- | --- | --- |
| `Categories` | `categories` | Store/launcher category hints |
| `Orientation` | `orientation` | Preferred orientation (`ManifestOrientation`) |
| `DisplayOverride` | `display_override` | Ordered fallback modes (e.g. `WindowControlsOverlay`) |
| `Shortcuts` | `shortcuts` | Home-screen / jump-list entries (`ManifestShortcut`) |
| `Screenshots` | `screenshots` | Richer install-UI previews (`ManifestScreenshot`) |
| `ShareTarget` | `share_target` | Receive content from the OS share sheet (`ShareTarget`) |
| `FileHandlers` | `file_handlers` | Open associated file types (`FileHandler`) |

```csharp
host.UsePwa(new WebAppManifest
{
    Name = "My Rask App",
    Icons = [new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable")],
    Categories = ["productivity"],
    Shortcuts = [new ManifestShortcut("New note", "/new", ShortName: "New")],
});
```

---

## Custom install button (`IInstallPrompt`)

By default the browser shows its own small "install" hint. To present your **own** install button
instead, inject `IInstallPrompt` (WASM-only). The framework captures the browser's
`beforeinstallprompt` event at boot and defers it, so you can replay it from a user gesture:

```csharp
using Rask.Wasm.Browser;

public sealed partial class InstallButton(IInstallPrompt install) : Component
{
    private bool _canInstall;

    protected override async Task OnRenderedAsync(bool first)
    {
        if (!first) return;
        _canInstall = !await install.IsInstalledAsync() && await install.CanInstallAsync();
        StateHasChanged();
    }

    protected override Component? Render() => _canInstall
        ? Button.OnClickAsync(Prompt)["Install app"]
        : Text("");

    private async Task Prompt()
    {
        var outcome = await install.PromptAsync();   // Accepted / Dismissed / Unavailable
        _canInstall = false;                          // the prompt is one-shot
        StateHasChanged();
    }
}
```

The browser only fires `beforeinstallprompt` when its install criteria are met (valid manifest,
service worker, HTTPS) and **once per page load**, so gate your button on `CanInstallAsync()` and hide
it once `IsInstalledAsync()` is true. iOS Safari has no `beforeinstallprompt` (users install via the
Share sheet), so `CanInstallAsync()` returns `false` there — keep your manual "Add to Home Screen"
hint as a fallback.

---

## Offline — the service worker

Rask ships a default service worker, **`rask-sw.js`**, served at the app root. It does two jobs:

1. **Offline app shell** — a network-first runtime cache (fresh when online, served from cache when
   offline), with navigations falling back to the cached shell so deep links work offline.
2. **Web Push** — shows the pushed notification and focuses/opens a window on click.

Register it from `index.html` (the `--pwa` templates do this for you). It resolves relative to
`<base href>`, so it works at the origin root and under a sub-path deploy:

```html
<script>
  if ("serviceWorker" in navigator) {
    window.addEventListener("load", function () {
      var base = document.querySelector("base");
      var scope = base ? new URL(base.href).pathname : "/";
      navigator.serviceWorker.register(scope + "rask-sw.js").catch(function () {});
    });
  }
</script>
```

Bring your own worker (custom caching/routing) by registering a different URL — e.g. via
`IWebPush.RegisterServiceWorkerAsync("/my-sw.js")`.

---

## Background sync (`IBackgroundSync`)

Ask the browser to wake the app **when connectivity returns** — so an edit made on a train is flushed
without the user coming back to the tab and waiting for a spinner. `IBackgroundSync` (in
`Rask.Wasm.Browser`, injected through the constructor) wraps both the
[Background Synchronization API](https://developer.mozilla.org/en-US/docs/Web/API/Background_Synchronization_API)
and [Periodic Background Sync](https://developer.mozilla.org/en-US/docs/Web/API/Web_Periodic_Background_Synchronization_API).
It rides the service worker above, so it is **WASM-only** and needs no extra wiring in a `--pwa` app.

### Know the boundary before you design around it

The browser fires the sync **even with the tab closed**. Rask's guarantee is narrower, and that gap is
the thing to design around: **the .NET runtime lives in the page, not in the service worker.** Your C#
runs only while a client is open. Rask's service worker forwards the woken-up tag to every open client;
with none open, the registration is consumed without your handler seeing it.

So:

- **Re-request your tags at boot.** A registration is best-effort, not durable queue state. Keep the
  work itself in `IIndexedDb` or OPFS and let the sync be the *nudge* to drain it, never the store.
- **The realistic win is a backgrounded tab, not a closed one.** A hidden or frozen tab is still a
  client, so it wakes and drains the moment the network is back — which is the case most offline-first
  apps actually hit.

Support is Chromium-only at the time of writing. Every call degrades to "unavailable" rather than
throwing, so a feature check is optional and a fallback is not.

### Draining an offline queue

```csharp
public sealed class DraftQueue(IBackgroundSync sync) : Component, IAsyncDisposable
{
    private IAsyncDisposable? _subscription;

    public override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender) return;

        // Subscribe BEFORE requesting. A sync that landed while the page was still booting is held for
        // the first subscriber, so an event that beat your startup code still reaches it.
        _subscription = await sync.OnSyncAsync(async e =>
        {
            if (e.Tag == "flush-drafts") await FlushAsync();
            StateHasChanged();
        });

        await sync.RequestSyncAsync("flush-drafts");   // best-effort, and re-requested every boot
    }

    public async ValueTask DisposeAsync() =>
        await (_subscription?.DisposeAsync() ?? ValueTask.CompletedTask);
}
```

`OnSyncAsync` is a subscription handler, not a chain-set callback, so calling `StateHasChanged()` in it
is correct and [RASK026](diagnostics.md) does not apply — the same rule as every other pushed API here.

### Periodic sync

Periodic sync is gated on a permission the browser grants on **its** terms (Chromium ties it to the app
being installed and to site engagement). There is no API to request it, so check, don't ask:

```csharp
if (await sync.IsPeriodicSupportedAsync() && await sync.GetPeriodicPermissionAsync() == "granted")
{
    // A floor, not a schedule: the browser decides the real cadence from engagement and battery, and
    // in practice fires far less often than you ask.
    await sync.RequestPeriodicSyncAsync("refresh-feed", TimeSpan.FromHours(12));
}
```

`OnSyncAsync` delivers both kinds — check `BackgroundSyncEvent.Periodic` to tell them apart.
`GetPendingTagsAsync()` / `GetPeriodicTagsAsync()` list what is registered, and
`UnregisterPeriodicAsync(tag)` removes a recurring one.

Full reference: [`IBackgroundSync`](apis/background-sync.md).

---

## Push notifications (`IWebPush`)

`IWebPush` (in `Rask.Core.Browser`, injected through the constructor) wraps the Web Push API. It works
on **both** hosts — on WASM always, and on Server once you opt in with
[`AddRaskPwa`](#pwa-on-the-server-host) (which serves the service worker push relies on). Drive it from
an event handler:

```csharp
public sealed partial class PushButton(IWebPush push) : Component
{
    private async Task Enable()
    {
        if (!await push.IsSupportedAsync()) return;
        if (await push.RequestPermissionAsync() != NotificationPermission.Granted) return;

        await push.RegisterServiceWorkerAsync();              // default rask-sw.js
        var sub = await push.SubscribeAsync(vapidPublicKey);  // your VAPID public key (base64url)

        // POST `sub` (Endpoint, P256dh, Auth) to your backend, which signs (VAPID) and encrypts
        // (RFC 8291) the push and delivers it to sub.Endpoint.
    }
}
```

**Sending** a push is the backend half of the Web Push Protocol — sign the request with VAPID and
encrypt the payload (RFC 8291). The opt-in **[`Rask.WebPush`](#sending-from-your-backend-raskwebpush)**
package does exactly that with zero external dependencies. The default `rask-sw.js` receives the push
and shows a notification from the JSON payload (`{ title, body, icon, tag, data: { url } }`).

### Sending from your backend (`Rask.WebPush`)

> Full reference: **[Rask.WebPush](webpush.md)**. The essentials:

`Rask.WebPush` is a small, server-side package (it has no UI and no transport dependency, so it works
from a `Rask.Server` app or the ASP.NET host behind a WASM PWA alike). Add it and register a sender:

```bash
dotnet add package Rask.WebPush
```

```csharp
using Rask.WebPush;

// Generate ONE VAPID key pair, then load it from configuration/secrets — never regenerate per run
// (that invalidates every existing subscription) and never ship the private key.
builder.Services.AddRaskWebPush(o =>
{
    o.VapidKeys = new VapidKeys(config["WebPush:PublicKey"]!, config["WebPush:PrivateKey"]!);
    o.Subject   = "mailto:admin@example.com";   // a contact the push service can reach
});
```

Hand the **same** `VapidKeys.PublicKey` to the client's `IWebPush.SubscribeAsync`. Store the
`PushSubscription` your client posts up, then deliver a notification with `IWebPushSender`:

```csharp
public sealed class Notifier(IWebPushSender sender, ISubscriptionStore store)
{
    public async Task PingAsync()
    {
        foreach (var sub in store.All)
        {
            var result = await sender.SendAsync(sub, WebPushMessage.Text(
                "New message", "You have one unread item.", url: "/inbox"));

            if (result.ShouldDelete) store.Remove(sub);   // 404/410 — the subscription is gone
            else if (result.ShouldRetry) { /* 429/5xx — retry later */ }
        }
    }
}
```

`VapidKeys.Generate()` mints a fresh pair (base64url) for first-time setup. The sender is transport-
neutral and stores nothing — persisting `PushSubscription`s is your app's job. See the full
subscribe → send → notify loop wired up in `samples/Rask.Example.Wasm.Host` (WASM) and
`samples/Rask.Example.Server` (Server).

---

## PWA on the Server host

A Rask **Server** app can be a PWA too — opt in with **`AddRaskPwa`**, the server-side counterpart to
the WASM host's `UsePwa`. One call makes the app installable and push-capable:

```csharp
using Rask.Core.Browser;
using Rask.Server;

builder.Services.AddRask();
builder.Services.AddRaskPwa(new WebAppManifest
{
    Name = "My Rask App",
    ShortName = "Rask App",
    ThemeColor = "#512BD4",
    Display = DisplayMode.Standalone,
    Icons = [new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable")]
});
```

`AddRaskPwa`:

- **serves the manifest** at `{PathBase}/rask/manifest.webmanifest` (relative URLs rooted at the app's
  base path) and emits the `<link rel="manifest">` + `<meta name="theme-color">` directly into the
  server-rendered `<head>` — no boot-time JS injection;
- **serves Rask's service worker** at `{PathBase}/rask-sw.js` and **auto-registers it**, so the app
  meets install criteria with no extra wiring;
- works with the transport-agnostic PWA APIs `AddRask()` already registers — `IWebPush`,
  `INotifications`, `IBadge`, `IWakeLock`.

Then ship a static **`wwwroot/offline.html`** (the SW serves it on failed navigations) and, to send
push, add **[`Rask.WebPush`](#sending-from-your-backend-raskwebpush)**. The Server showcase
(`samples/Rask.Example.Server`, the **Server PWA** page) wires the whole loop.

> **What you don't get on Server.** A Server app renders over a live WebSocket, so it is **not an
> offline app**: the service worker deliberately does **not** cache the server-rendered shell (it
> carries a one-shot session id and is served `no-store`), so offline navigations show `offline.html`
> rather than a dead cached page. The **install-prompt replay** (`IInstallPrompt`) and the
> activation-bound imperative device APIs (`IShare`, `IFullscreen`, `IMediaDevices`, …) are not
> registered on Server, and neither is [**background sync**](#background-sync-ibackgroundsync) — it rides
> the service-worker registration and needs a client-side runtime to wake into, which a WebSocket-rendered
> app does not have. The honest framing: *installable + push + native-feel, not an offline app.* (Sharing still works on Server via the headless `Shareable` in `Rask.Core`,
> which fires `navigator.share` in the click gesture; the imperative `IShare` lives in `Rask.Client`, WASM +
> Native only.)

---

## Device capabilities for mobile

Typed wrappers for the browser APIs that make a web app feel native. Everything in `Rask.Core.Browser`
works on **both transports** (and is registered on Server too) — including the PWA APIs `IWebPush`,
`INotifications`, `IBadge`, `IWakeLock`, and the headless declarative `Shareable` *(all hosts)*. The imperative
`*(WASM + Native)*` ones (`IShare`) live in `Rask.Client.Browser` and run on the in-process WASM and Native
hosts; the `*(WASM)*` ones live in `Rask.Wasm.Browser`. Both need a live user gesture or the installed-app instance the Server round-trip
can't carry, so neither is registered on Server.

| Capability | Service | Use |
| --- | --- | --- |
| **Share sheet** | `Shareable` *(all)* / `IShare` *(WASM + Native)* | Headless declarative share works everywhere; imperative `IShare` for code-driven shares (native backend on Native) |
| **Vibration** | `IVibration` | Haptic feedback (`VibrateAsync(200)`) |
| **Geolocation** | `IGeolocation` | Current position (`GetCurrentPositionAsync`) + live tracking (`WatchAsync`) |
| **Clipboard** | `IClipboard` | Copy/paste |
| **Storage / Cookies** | `IBrowserStorage` / `ICookies` | Persist state on-device |
| **Large storage** | `IIndexedDb` | Async key/value store backed by IndexedDB — cache app data offline |
| **Files on disk** | `IFileSystemAccess` | Open/save a file back to disk + directory access (editors, file managers) |
| **Passkeys** | `IWebAuthn` | Passwordless register / sign-in with a biometric or security key |
| **Permissions** | `IPermissions` | Check before prompting |
| **Page visibility** | `IPageVisibility` | Pause work when backgrounded |
| **Online status** | `INavigatorInfo` | `OnLineAsync()` for an offline indicator |
| **Network quality** | `INetworkInfo` | `GetStatusAsync()` → effective type / downlink / Data Saver, to adapt loading |
| **Media queries** | `IMediaQuery` | `MatchesAsync(query)` / `PrefersDarkAsync` / `PrefersReducedMotionAsync` |
| **Speech (text-to-speech)** | `ISpeechSynthesis` | `SpeakAsync(text, SpeechOptions?)` / `CancelAsync` |
| **Screen info** | `IScreenInfo` | `GetAsync()` → size / color depth / device pixel ratio (retina) |
| **Storage estimate** | `IStorageEstimator` | `EstimateAsync()` → quota / usage, to budget offline caches |
| **Visual viewport** | `IVisualViewport` | `GetAsync()` → visible size/offset/zoom, e.g. above the soft keyboard |
| **Cross-tab messaging** | `IBroadcastChannel` | `OpenAsync(name, onMessage)` / `PostAsync` — sync sign-out, theme, "data updated" across tabs |
| **Local notifications** | `INotifications` | Show a notification from the page (no server) |
| **App badge** | `IBadge` | Unread count on the installed icon (`SetAsync(3)` / `ClearAsync()`) |
| **Wake lock** | `IWakeLock` | Keep the screen awake; dispose the sentinel to release |
| **Screen orientation** | `IScreenOrientation` *(WASM)* | Read orientation; `LockAsync` / `UnlockAsync` (needs fullscreen) |
| **Fullscreen** | `IFullscreen` *(WASM)* | Present an element/page fullscreen (`RequestAsync(ElementRef?)` / `ExitAsync`) |
| **Camera / mic / screen** | `IMediaDevices` *(WASM)* | Capture into a `<video>` (`GetUserMediaAsync` / `GetDisplayMediaAsync`) |
| **Picture-in-Picture** | `IPictureInPicture` *(WASM)* | Float a `<video>` into an always-on-top miniplayer |
| **Gamepad** | `IGamepad` | Read connected controllers — sticks / triggers / buttons (`WatchAsync`) |
| **Idle detection** | `IIdleDetector` *(WASM)* | Auto-lock / presence when the user goes idle or the screen locks |
| **EyeDropper** | `IEyeDropper` *(WASM)* | Pick a color from anywhere on screen (`OpenAsync`) |
| **Serial device** | `ISerial` *(WASM)* | Talk to an Arduino / serial device — `RequestPortAsync(options, onData, onClosed?)` → `ISerialPort?` |
| **USB device** | `IUsb` *(WASM)* | Pair with and drive a USB device — `RequestDeviceAsync(filters)` → `IUsbDevice?` (open, claim, transfer) |
| **HID device** | `IHid` *(WASM)* | Talk to a HID device — `RequestDevicesAsync(filters)` → devices (output/feature reports + pushed input reports) |
| **Bluetooth (BLE)** | `IBluetooth` *(WASM)* | Pair with a BLE device — `RequestDeviceAsync(options)` → connect GATT, read/write/notify characteristics |
| **Background sync** | `IBackgroundSync` *(WASM)* | Wake the app to drain an offline queue when connectivity returns, or on a schedule |

**App badge.** `IBadge` (`Rask.Core.Browser`) sets a count on the **installed** app's icon —
`SetAsync(count)` (or `SetAsync()` for a plain dot) and `ClearAsync()`. A silent no-op in a normal
browser tab, so gate on `IsSupportedAsync()`. Pairs with notifications/push to surface an unread count.

**Wake lock.** `IWakeLock.RequestAsync()` returns an `IWakeLockSentinel`; keep it while the screen
should stay on and `DisposeAsync()` (e.g. `await using`, or from a component's `DisposeAsync`) to
release. Browsers auto-release when the page is hidden — the framework re-acquires held locks when it
becomes visible again, so a sentinel stays effective until you dispose it.

**Screen orientation.** `IScreenOrientation.GetAsync()` reads the current `OrientationInfo`
(type + angle); `LockAsync(OrientationLock.Landscape)` / `UnlockAsync()` lock it — locking usually
requires fullscreen and is often unsupported on desktop, so wrap it in `try/catch`.

**Fullscreen.** `IFullscreen.RequestAsync(element)` presents an `ElementRef` (or, with no argument, the
whole page) fullscreen; `ExitAsync()` leaves and `IsActiveAsync()` reports state. `requestFullscreen`
needs a live user gesture, so call it from an event handler and gate on `IsSupportedAsync()`. Request
fullscreen first when you also want to **lock the orientation** — most browsers only allow the lock in
fullscreen.

**Local vs push notifications.** `INotifications` (`Rask.Core.Browser`) shows a notification directly
from the running page — `RequestPermissionAsync()` then `ShowAsync(title, new NotificationOptions { … })`.
Use it for in-app alerts. For notifications delivered while the app is **closed**, use
[`IWebPush`](#push-notifications-iwebpush) — those go through the service worker.

See [JS interop → Typed browser APIs](js-interop-runtime.md#typed-browser-apis) for the full surface.

---

## Deploying (GitHub Pages & sub-paths)

Publish with `RaskPathBase` so the `<base href>`, manifest, and service-worker scope resolve under
the sub-path:

```bash
dotnet publish -c Release /p:RaskPathBase=/my-repo
```

The manifest's relative `start_url`/`scope` and the `<base href>`-relative SW registration handle the
prefix automatically — the published app is installable and offline at `https://you.github.io/my-repo/`.
The Rask showcase itself is a deployed WASM PWA — install it from
[the live demo](https://pal-tamas.github.io/rask/docs/).
