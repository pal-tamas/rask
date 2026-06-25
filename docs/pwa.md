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

A `wwwroot/manifest.webmanifest` plus a `<link rel="manifest">` makes the app installable (the
browser's "Install app" / "Add to Home Screen"):

```json
{
  "name": "My Rask App",
  "short_name": "Rask App",
  "start_url": ".",
  "scope": ".",
  "display": "standalone",
  "background_color": "#faf9fe",
  "theme_color": "#512BD4",
  "icons": [
    { "src": "icon.svg", "sizes": "any", "type": "image/svg+xml", "purpose": "any maskable" }
  ]
}
```

Relative `start_url`/`scope` (`"."`) keep it correct under a sub-path deploy (GitHub Pages). In
`wwwroot/index.html`:

```html
<link href="manifest.webmanifest" rel="manifest"/>
<meta content="#512BD4" name="theme-color"/>
```

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
`Rask.Core.Browser` (work on both transports); `IShare` is WASM-only (it needs a live user gesture).

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
