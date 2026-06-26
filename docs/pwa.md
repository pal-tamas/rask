# 📱 Mobile apps with Rask — PWA, offline & push

**Build installable, offline, native-feeling mobile apps in C# — no Swift, Kotlin, React Native, or
MAUI.** A Rask **WASM** app is a Progressive Web App: it installs to the home screen, launches
full-screen, works offline, sends push notifications, and reaches device capabilities (vibration,
share sheet, geolocation, clipboard) through typed C# — the same component code you already write.

> PWA features are **WASM-only**. Offline, install, and push run independently of any server, which
> the Server/WebSocket transport can't provide. See
> [When to use Server vs WASM](../README.md#-when-to-use-server-vs-wasm).

- [Make your app a PWA](#make-your-app-a-pwa)
- [Installable — the web app manifest](#installable--the-web-app-manifest)
- [Offline — the service worker](#offline--the-service-worker)
- [Push notifications (`IWebPush`)](#push-notifications-iwebpush)
- [Device capabilities for mobile](#device-capabilities-for-mobile)
- [Deploying (GitHub Pages & sub-paths)](#deploying-github-pages--sub-paths)

---

## Make your app a PWA

Start a new app with the **`--pwa`** option and it's installable + offline out of the box:

```bash
dotnet new rask-wasm --pwa          # standalone browser-WASM PWA
dotnet new rask-wasm-hosted --pwa   # WASM PWA + ASP.NET host
```

That scaffolds a web app manifest + icon and registers Rask's default service worker from
`index.html`. To add it to an existing Rask WASM app, do the two steps below by hand
(manifest + service-worker registration).

---

## Installable — the web app manifest

Configure a typed `WebAppManifest` in `Program.cs` — the framework injects the
`<link rel="manifest">` (a `data:` URL, so **no `manifest.webmanifest` file to ship**) and the
`<meta name="theme-color">` at boot. There's nothing to hand-write or keep in sync:

```csharp
using Rask.Wasm.Browser;

var host = WasmHostBuilder.CreateDefault();
host.UseManifest(new WebAppManifest
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

## Push notifications (`IWebPush`)

`IWebPush` (in `Rask.Wasm.Browser`, injected through the constructor) wraps the Web Push API. Drive
it from an event handler:

```csharp
public sealed class PushButton(IWebPush push) : Component
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

**Sending** a push is a backend concern (the Web Push Protocol — VAPID signing + payload
encryption), outside Rask: store the `PushSubscription` and send with any web-push library. The
default `rask-sw.js` receives it and shows a notification from the JSON payload
(`{ title, body, icon, tag, data: { url } }`).

---

## Device capabilities for mobile

Typed wrappers for the browser APIs that make a web app feel native. The shared ones live in
`Rask.Core.Browser` (work on both transports); the `*(WASM)*` ones live in `Rask.Wasm.Browser` because
they need a live user gesture or the installed-app instance the Server round-trip can't carry.

| Capability | Service | Use |
| --- | --- | --- |
| **Share sheet** | `IShare` *(WASM)* | Hand a link/text to the OS share UI |
| **Vibration** | `IVibration` | Haptic feedback (`VibrateAsync(200)`) |
| **Geolocation** | `IGeolocation` | Current position |
| **Clipboard** | `IClipboard` | Copy/paste |
| **Storage / Cookies** | `IBrowserStorage` / `ICookies` | Persist state on-device |
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
| **Local notifications** | `INotifications` *(WASM)* | Show a notification from the page (no server) |
| **App badge** | `IBadge` *(WASM)* | Unread count on the installed icon (`SetAsync(3)` / `ClearAsync()`) |
| **Wake lock** | `IWakeLock` *(WASM)* | Keep the screen awake; dispose the sentinel to release |
| **Screen orientation** | `IScreenOrientation` *(WASM)* | Read orientation; `LockAsync` / `UnlockAsync` (needs fullscreen) |
| **Fullscreen** | `IFullscreen` *(WASM)* | Present an element/page fullscreen (`RequestAsync(ElementRef?)` / `ExitAsync`) |

**App badge.** `IBadge` (`Rask.Wasm.Browser`) sets a count on the **installed** app's icon —
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

**Local vs push notifications.** `INotifications` (`Rask.Wasm.Browser`) shows a notification directly
from the running page — `RequestPermissionAsync()` then `ShowAsync(title, new NotificationOptions { … })`.
Use it for in-app alerts. For notifications delivered while the app is **closed**, use
[`IWebPush`](#push-notifications-iwebpush) — those go through the service worker.

See [JS interop → Typed browser APIs](js-interop.md#typed-browser-apis) for the full surface.

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
[the live demo](https://pal-tamas.github.io/rask/).
