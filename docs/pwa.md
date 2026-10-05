# 📱 Mobile apps with Rask — PWA, offline & push

**Build installable, offline, native-feeling mobile apps in C# — no Swift, Kotlin, React Native, or
MAUI.** A Rask **WASM** app is a Progressive Web App: it installs to the home screen, launches
full-screen, works offline, sends push notifications, and reaches device capabilities (vibration,
share sheet, geolocation, clipboard) through typed C# — the same component code you already write.

> **WASM vs Server.** A WASM app gets the *full* PWA: install, **true offline**, push, and every device
> API. A **Server** app (opt in with `AddRaskPwa`) is **installable + push-capable** — manifest, Web Push
> subscribe, local notifications, app badge, and wake lock all work — but it is **not an offline app**:
> it renders over a live WebSocket, so offline navigations show a static offline page, a custom install button
> uses the declarative `Trigger.Install` (the imperative `Prompt()` stays WASM-only), and there is no
> **background sync** (WASM-only). See
> [choosing a host template](getting-started.md#1-scaffold-a-project) and
> [PWA on the Server host](#pwa-on-the-server-host) below.

- [Make your app a PWA](#make-your-app-a-pwa)
- [Installable — the web app manifest](#installable--the-web-app-manifest)
- [Custom install button (`Window.OnBeforeInstallPrompt`)](#custom-install-button-windowonbeforeinstallprompt)
- [Offline — the service worker](#offline--the-service-worker)
- [Background sync (`IBackgroundSync`)](#background-sync-ibackgroundsync)
- [Push notifications (`PushManager`)](#push-notifications-pushmanager)
- [PWA on the Server host](#pwa-on-the-server-host)
- [Device capabilities for mobile](#device-capabilities-for-mobile)
- [Deploying (GitHub Pages & sub-paths)](#deploying-github-pages--sub-paths)

---

## Make your app a PWA

Start a new app with the **`--pwa`** option:

```bash
rask new MyApp --template wasm                # standalone browser-WASM PWA (full offline)
rask new MyApp                                # installable + push-capable Server app (not offline)
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
hand-write or keep in sync. A `theme-color` your page's head declares (including a light/dark `media`
pair) wins: the manifest's `ThemeColor` is added only when the page names none, so the browser's toolbar
tint never switches colour as the app boots.

```csharp
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
await host.Run<App>();
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

## Custom install button (`Window.OnBeforeInstallPrompt`)

By default the browser shows its own small "install" hint. To present your **own** install button
instead, listen for MDN's `beforeinstallprompt` with `Window.OnBeforeInstallPrompt` from
[`Rask.Web`](web-apis.md#where-a-browser-falls-short). The browser fires it once, at load, before any component
can subscribe, so Rask keeps it from boot and hands it to each later subscriber until it is spent:

```csharp
using Rask.Web;

public sealed partial class InstallButton : Component
{
    private Rask.Web.Types.BeforeInstallPromptEvent? _prompt;
    private IAsyncDisposable? _offer;

    protected override async Task OnFirstRender()
    {
        if (await Window.MatchMedia("(display-mode: standalone)").Matches) return;   // already installed
        _offer = await Window.OnBeforeInstallPrompt(e => _prompt = e);
    }

    protected override Component? Render() => _prompt is not null
        ? Button.OnClick(Prompt)["Install app"]
        : Text("");

    private async Task Prompt()
    {
        var answer = await _prompt!.Prompt();
        var accepted = answer.UserChoice == AppBannerPromptOutcome.Accepted;
        _prompt = null;                                    // the prompt is one-shot
    }

    protected override async Task OnUnmount()
    {
        if (_offer is not null) await _offer.DisposeAsync();
    }
}
```

`Prompt()` needs the click, so it runs in a **WASM** app. On the Server host, the declarative
`Trigger.Install` shows the same kept prompt inside the click. `Window.OnAppInstalled(…)` tells you when the
user installed the app.

The browser only fires `beforeinstallprompt` when its install criteria are met (valid manifest,
service worker, HTTPS) and **once per page load**, so show your button only once the event has arrived, and hide
it when `(display-mode: standalone)` matches. iOS Safari has no `beforeinstallprompt` (users install via the
Share sheet), so the handler never runs there — keep your manual "Add to Home Screen" hint as a fallback.

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

Bring your own worker (custom caching/routing) by registering a different URL there instead. Whichever
worker the page registered is the one `await Navigator.ServiceWorker.Ready` answers, so push follows it.

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

    public override async Task OnFirstRender()
    {
        // Subscribe BEFORE requesting. A sync that landed while the page was still booting is held for
        // the first subscriber, so an event that beat your startup code still reaches it.
        _subscription = await sync.OnSync(async e =>
        {
            if (e.Tag == "flush-drafts") await FlushAsync();
            StateHasChanged();
        });

        await sync.RequestSync("flush-drafts");   // best-effort, and re-requested every boot
    }

    public async ValueTask DisposeAsync() =>
        await (_subscription?.DisposeAsync() ?? ValueTask.CompletedTask);
}
```

`OnSync` is a subscription handler, not a chain-set callback, so calling `StateHasChanged()` in it
is correct and [RASK026](diagnostics.md) does not apply — the same rule as every other pushed API here.

### Periodic sync

Periodic sync is gated on a permission the browser grants on **its** terms (Chromium ties it to the app
being installed and to site engagement). There is no API to request it, so check, don't ask:

```csharp
if (await sync.IsPeriodicSupported() && await sync.GetPeriodicPermission() == "granted")
{
    // A floor, not a schedule: the browser decides the real cadence from engagement and battery, and
    // in practice fires far less often than you ask.
    await sync.RequestPeriodicSync("refresh-feed", 12.Hours);
}
```

`OnSync` delivers both kinds — check `BackgroundSyncEvent.Periodic` to tell them apart.
`GetPendingTags()` / `GetPeriodicTags()` list what is registered, and
`UnregisterPeriodic(tag)` removes a recurring one.

Full reference: [`IBackgroundSync`](apis/background-sync.md).

---

## Push notifications (`PushManager`)

A browser subscribes to Web Push through MDN's own [`PushManager`](https://developer.mozilla.org/docs/Web/API/PushManager),
from [`Rask.Web`](web-apis.md) — there is no Rask wrapper. It hangs off the service worker the page already
registered (`rask-sw.js`: the `--pwa` templates' `index.html` on WASM, [`AddRaskPwa`](#pwa-on-the-server-host)'s
`<head>` on Server), so the subscription starts at `Navigator.ServiceWorker.Ready`. Drive it from an event handler:

```csharp
using System.Buffers.Text;

public sealed partial class PushButton : Component
{
    private async Task Enable()
    {
        // Settles once rask-sw.js is active; the registration is a kept browser object, so dispose of it.
        await using var worker = await Navigator.ServiceWorker.Ready;
        await using var subscription = await worker.PushManager.Subscribe(new()
        {
            UserVisibleOnly = true,                                               // every push shows a notification
            ApplicationServerKey = Base64Url.DecodeFromChars(Push.PublicKey),     // the app's VAPID public key
        });
        await Push.Subscribe(await subscription.ToJSON());                        // kept on the app's database
    }
}
```

`Subscribe` asks for notification permission itself; on WASM, `await Notification.RequestPermission()` in the click
asks first. Subscribing again with the same key answers the subscription the browser already has, and
`await subscription.Unsubscribe()` removes it. A WebAssembly client has no `Push` battery in its process: it reads
the key from `GET /_rask/push/key` and posts `await subscription.ToJSON()` — MDN's `PushSubscriptionJSON`, as is — to
`POST /_rask/push/subscribe`.

The default `rask-sw.js` receives a push and shows a notification from its JSON payload
(`{ title, body, icon, tag, data: { url } }`), so nothing else runs in the browser.

### Sending from your backend (`Rask.WebPush`)

> Full reference: **[Rask.WebPush](webpush.md)**. The essentials:

> Included in [`Rask.Server`](../README.md) — nothing to install. It is **on**; an app that does without it says so:
>
> ```csharp
> app.Configure(c => c.Push.Off());
> ```

The subscriptions live in a table on the app's own database, so a send is one line:

```csharp
await Push.Send(WebPushMessage.Text("New message", "You have one unread item.", "/inbox"));
await Push.Send(WebPushMessage.Text("Your order shipped")).To(userId);   // one person's devices
```

A subscription the push service says is gone is dropped as the send finds it. A WebAssembly client posts its
`subscription.ToJSON()` to `POST /_rask/push/subscribe` instead of calling `Push.Subscribe`, and reads the public key
from `GET /_rask/push/key`.

The keys come from `Rask:Push`: `rask new` wrote a development pair to the gitignored
`appsettings.Development.json`, and deployed they come from the environment
(`Rask__Push__VapidKeys__PublicKey` / `__PrivateKey`). The contact is `Rask:Push:Subject`, a `mailto:` or
`https:` address. `VapidKeys.Generate()` mints a pair; never regenerate a live one.

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
- works with MDN's `Navigator.WakeLock`, `PushManager`, `Notification` and `Navigator.SetAppBadge` from
  [`Rask.Web`](web-apis.md).

Then ship a static **`wwwroot/offline.html`** (the SW serves it on failed navigations) and, to send
push, add **[`Rask.WebPush`](#sending-from-your-backend-raskwebpush)**.

> **What you don't get on Server.** A Server app renders over a live WebSocket, so it is **not an
> offline app**: the service worker deliberately does **not** cache the server-rendered shell (it
> carries a one-shot session id and is served `no-store`), so offline navigations show `offline.html`
> rather than a dead cached page. The activation-bound imperative calls (the install event's `Prompt()`,
> `GetDisplayMedia()`, `RequestFullscreen()`, …) are not available on Server (`Trigger.Install` and the other
> gesture triggers run them in the click instead), and neither is [**background sync**](#background-sync-ibackgroundsync) — it rides
> the service-worker registration and needs a client-side runtime to wake into, which a WebSocket-rendered
> app does not have. The honest framing: *installable + push + native-feel, not an offline app.* (Sharing
> still works on Server via the headless `Shareable` in `Rask.Core`, which fires `navigator.share` in the
> click gesture; the imperative `Navigator.Share(…)` from `Rask.Web` compiles only in a WASM app.)

---

## Device capabilities for mobile

The browser APIs that make a web app feel native. Rows marked *(Rask.Web)* are MDN's own surface from
[`Rask.Web`](web-apis.md) (imported for you); the rest are typed wrappers. Everything in `Rask.Core.Browser`
works on **both transports** (and is registered on Server too), as do the screen wake lock from `Rask.Web` and the
headless declarative `Shareable` *(all hosts)*. The
`*(WASM)*` ones need a live user gesture or the installed-app instance the Server round-trip can't carry: the
device/handle set lives in `Rask.Wasm.Browser`, and none is registered on Server.

| Capability | API | Use |
| --- | --- | --- |
| **Share sheet** | `Shareable` *(all)* / `Navigator.Share(…)` *(Rask.Web, WASM)* | Headless declarative share works everywhere; `Navigator.Share` for code-driven shares |
| **Vibration** | `Navigator.Vibrate(200)` *(Rask.Web)* | Haptic feedback |
| **Geolocation** | `Navigator.Geolocation` *(Rask.Web)* | Current position (`GetCurrentPosition`) + live tracking (`WatchPosition`) |
| **Clipboard** | `Navigator.Clipboard` *(Rask.Web)* | Copy/paste (`WriteText` / `ReadText`) |
| **Storage / Cookies** | `LocalStorage` / `Document.Cookie` *(Rask.Web)* | Persist state on-device |
| **Large storage** | `IIndexedDb` | Async key/value store backed by IndexedDB — cache app data offline |
| **Files on disk** | `Window.ShowOpenFilePicker(…)` *(Rask.Web, WASM)* | Open a file, then `ShowSaveFilePicker` / `CreateWritable()` to save it back (editors, file managers) |
| **Passkeys** | `IWebAuthn` | Passwordless register / sign-in with a biometric or security key |
| **Permissions** | `Navigator.Permissions.Query(…)` *(Rask.Web)* | Check before prompting |
| **Page visibility** | `Document.VisibilityState` *(Rask.Web)* | Pause work when backgrounded |
| **Online status** | `Navigator.OnLine` *(Rask.Web)* | An offline indicator |
| **Network quality** | `Navigator.Connection` *(Rask.Web)* | `EffectiveType` / `Downlink` / `SaveData`, to adapt loading |
| **Media queries** | `Window.MatchMedia(query)` *(Rask.Web)* | `.Matches`, and `OnChange` to follow it |
| **Speech (text-to-speech)** | `SpeechSynthesis.Speak(utterance)` *(Rask.Web)* | Speak a `SpeechSynthesisUtterance`; `Cancel()` stops it |
| **Screen info** | `Screen` *(Rask.Web)* | `Width` / `Height` / `ColorDepth`; `Window.DevicePixelRatio` for retina |
| **Storage estimate** | `Navigator.Storage.Estimate()` *(Rask.Web)* | `Quota` / `Usage`, to budget offline caches; `Persist()` to survive eviction |
| **Visual viewport** | `Window.VisualViewport` *(Rask.Web)* | Visible size/offset/zoom, e.g. above the soft keyboard |
| **Cross-tab messaging** | `BroadcastChannel.Create(name)` *(Rask.Web)* | `PostMessage` / `OnMessage` — sync sign-out, theme, "data updated" across tabs |
| **Local notifications** | `Notification.Create(title, …)` *(Rask.Web)* | Show a notification from the page (no server); `Notification.RequestPermission()` *(WASM)* first |
| **App badge** | `Navigator.SetAppBadge(3)` *(Rask.Web)* | Unread count on the installed icon (`SetAppBadge(3)` / `ClearAppBadge()`) |
| **Wake lock** | `Navigator.WakeLock.Request(WakeLockType.Screen)` *(Rask.Web)* | Keep the screen awake; `Release()` the sentinel to let it sleep |
| **Device tilt / motion** | `Window.OnDeviceOrientation` / `OnDeviceMotion` *(Rask.Web)* | Gyroscope and accelerometer events; `every:` throttles them in the browser |
| **Screen orientation** | `Screen.Orientation` *(Rask.Web)* | Read orientation; `Lock(…)` *(WASM)* / `Unlock()` (needs fullscreen) |
| **Fullscreen** | `_stage.RequestFullscreen()` *(Rask.Web, WASM)* | Present an element fullscreen; `Document.ExitFullscreen()` leaves |
| **Camera / mic / screen** | `Navigator.MediaDevices` *(Rask.Web)* | `GetUserMedia(…)`, or `GetDisplayMedia()` *(WASM)*; show it with `_video.SetSrcObject(stream)` |
| **Picture-in-Picture** | `_video.RequestPictureInPicture()` *(Rask.Web, WASM)* | Float a `<video>` into an always-on-top miniplayer |
| **Gamepad** | `Navigator.GetGamepads()` *(Rask.Web)* | Read connected controllers — `Buttons` / `Axes`; `Window.OnGamepadConnected` |
| **Idle detection** | `IdleDetector.Create()` *(Rask.Web, WASM)* | Auto-lock / presence when the user goes idle or the screen locks |
| **EyeDropper** | `EyeDropper.Create()` *(Rask.Web, WASM)* | Pick a color from anywhere on screen (`Open()`) |
| **Serial device** | `Navigator.Serial` *(Rask.Web, WASM)* | Talk to an Arduino / serial device — `RequestPort(…)`, then `Open(…)`, `Readable.GetReader()` / `Writable.GetWriter()` |
| **USB device** | `Navigator.Usb` *(Rask.Web, WASM)* | Pair with and drive a USB device — `RequestDevice(…)`, then `Open()` / `TransferIn` / `TransferOut` |
| **HID device** | `Navigator.Hid` *(Rask.Web, WASM)* | Talk to a HID device — `RequestDevice(…)`, then `SendReport` / `OnInputReport` |
| **Bluetooth (BLE)** | `Navigator.Bluetooth` *(Rask.Web, WASM)* | Pair with a BLE device — `RequestDevice(…)`, then read a characteristic's `ReadValue()` as `byte[]` |
| **Background sync** | `IBackgroundSync` *(WASM)* | Wake the app to drain an offline queue when connectivity returns, or on a schedule |

**App badge.** `await Navigator.SetAppBadge(3)` from [`Rask.Web`](web-apis.md) sets a count on the **installed**
app's icon. `SetAppBadge()` shows a plain dot and `ClearAppBadge()` removes it. It does nothing in a normal browser
tab. Pair it with notifications or push to show an unread count.

**Wake lock.** `_sentinel = await Navigator.WakeLock.Request(WakeLockType.Screen)` from
[`Rask.Web`](web-apis.md#where-a-browser-falls-short) keeps the screen on, on both hosts, with no click needed. Keep
the sentinel while the screen should stay on, then `await _sentinel.Release()` and `await _sentinel.DisposeAsync()`.
Disposing alone does **not** release the lock, just as dropping a sentinel in JS doesn't. Every browser drops the
lock when the page is hidden; Rask takes it again each time the page becomes visible, so the sentinel holds until
you release it. `Released` turns true and `OnRelease` fires once — when you release it, or when the browser refuses
it back.

**Screen orientation.** `await Screen.Orientation.Type` and `await Screen.Orientation.Angle` read it.
`await Screen.Orientation.Lock(…)` (WASM, in a click) and `Unlock()` lock it — locking usually requires fullscreen
and is often unsupported on desktop, so wrap it in `try/catch`. On the Server host, use `Trigger.ScreenOrientation`.

**Fullscreen.** `await _stage.RequestFullscreen()` from [`Rask.Web`](web-apis.md#on-an-element-ref) presents an
element ref fullscreen (WASM, in the click). `await Document.ExitFullscreen()` leaves. `await Document.FullscreenEnabled`
says whether it can, and `await Document.FullscreenElement == _stage` whether it is yours. On the Server host, use
`Trigger.Fullscreen`. Request fullscreen first when you also want to **lock the orientation** — most browsers only
allow the lock in fullscreen.

**Camera and microphone.** `await Navigator.MediaDevices.GetUserMedia(new() { Video = new() { Width = 640, FacingMode = "user" } })`
from [`Rask.Web`](web-apis.md#keeping-an-object) asks for the camera and hands back a kept `MediaStream`.
`await _video.SetSrcObject(stream)` shows it. Stop it with `await stream.GetTracks()` and `Stop()` on each track.
Screen capture is `GetDisplayMedia()` (WASM, in the click). On the Server host, `Trigger.MediaCapture` runs the
camera in the click too: `.OnStream(stream => _camera = MediaStream.From(stream))` keeps the stream, and
`foreach (var t in await _camera.GetTracks()) await t.Stop();` stops it.

**Device tilt and motion.** `await Window.OnDeviceOrientation(e => _angle = e.Alpha, every: 100.Milliseconds)` and
`Window.OnDeviceMotion(e => …, every: …)` from [`Rask.Web`](web-apis.md#events-and-callbacks) follow the gyroscope
and accelerometer. `every:` throttles the events in the browser before they cross. Dispose of the subscription to
stop. iOS asks first: `await DeviceOrientationEvent.RequestPermission()` (WASM, in the click).

**Local vs push notifications.** `Notification` from [`Rask.Web`](web-apis.md) shows a notification directly from
the running page. Ask first with `await Notification.RequestPermission()` (WASM, in the click), then
`await using var n = await Notification.Create("Title", new() { Body = "…" })`. `await Notification.Permission` reads
the answer. Use it for in-app alerts. For notifications delivered while the app is **closed**, use
[`PushManager`](#push-notifications-pushmanager) — those go through the service worker.

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
[the live demo](https://rask.sh/docs/).
