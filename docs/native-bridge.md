# Native — modes & the JS bridge

Local vs Server hosting, the `INativeWebView` contract, wiring a platform head, and serving a full app's assets on-device.

‹ Back to [Native mobile apps](native.md)

## Two modes: Local and Server

```csharp
using Rask.Native;

// Native + Local — the app runs in-process on the device (offline, store-distributable).
var host = NativeAppHost.CreateDefault();
// host.Services.AddSingleton<IMyService, MyService>();   // register app services
NativeApp app = await host.RunLocalAsync<App>(webView);   // webView: your INativeWebView

// Native + Server — the WebView is a thin native shell over a remote Rask Server (wss://).
NativeServerShell shell = NativeAppHost.ConnectToServer(new Uri("https://app.example.com/"));
// the platform head navigates its WebView to shell.ServerBaseUrl; the server serves its own client.
```

- **Local** is the offline native app: the whole app (routing, state, handlers) runs on-device; navigations
  and state changes never touch the network. `RunLocalAsync` builds the DI container, wraps your `App` in a
  `RootErrorBoundary`, seeds the route, and wires the WebView. The first render fires when the WebView's
  client posts its `ready` message, so it's safe to call before the WebView finishes loading.
- **Server** makes the device a native, store-distributable shell over a server-driven app — the same
  `Rask.Server` app, now installable with native device APIs available to the page (see below).

> **Only one of the two hot-reloads.** Applying new IL to an app already running on a device needs a
> device-side delta agent that .NET doesn't ship, and `dotnet watch` can't drive a simulator either — so a
> **Local** head has to be restarted (`dotnet build -t:Run`) to pick up a C# edit. A **Server** head is a
> browser onto your server, so it hot-reloads like any other page: point `ConnectToServer` at your dev
> machine (`http://10.0.2.2:<port>` from the Android emulator — with the cleartext caveat below —
> `http://localhost:<port>` from the iOS simulator), run [`rask dev`](cli.md#what-hot-reloads) against the
> *server* project, and every applied edit repaints the device, "Hot reload applied" pill included.

### Native device APIs from a Server app (the capability bridge)

In Server mode the C# runs on the server and the device is a WebView, so a mid-handler `IShare` call can't
work — but a plain **`Shareable`** button still pops the **native** sheet, because the head injects the
[capability bridge](native-devices.md#native-device-backends) into the page. Scaffold the Server-mode head with the
`--host` parameter:

```bash
rask new MyApp --template native --host server   # (--host local is the default)
```

The generated head (`Platforms/Android/ServerActivity.cs`, `Platforms/iOS/ServerAppDelegate.cs`) points its
WebView at your `ConnectToServer(...)` URL and wires the bridge with the two `NativeCapabilities` helpers:

- **`NativeCapabilities.BridgeScript`** — injected at each navigation **only for your trusted origin**, so
  the page sees `window.__raskNative.capabilities` + `invoke(name, data)`.
- **`NativeCapabilities.TryHandleAsync(messageJson, share)`** — the WebView's script-message handler routes
  every posted message here with a native `IShare` (the scaffold's `NativeShare`).

Now the *same* `Shareable` component that renders on the server fires the device's native
`UIActivityViewController` / `Intent.ACTION_SEND` when run in the shell — the "superpower". **Security:** the
generated head injects `BridgeScript` only for your origin and opens off-origin links in the system browser
(`WKNavigationDelegate.decidePolicyForNavigationAction` / `shouldOverrideUrlLoading`), so the WebView never
leaves your origin and no other page can reach native; the bridge is a fixed component envelope, not open
native RPC. (For a local `http://10.0.2.2:<port>` dev server, allow cleartext in `AndroidManifest.xml`.)

## The `INativeWebView` bridge

`Rask.Native` **ships the platform WebView heads** — `RaskWkWebView` (iOS, `WKWebView`) and
`RaskAndroidWebView` (Android, `android.webkit.WebView`) — under `Platforms/{iOS,Android}`, built when the
package is packed with the mobile workloads. Your app head just news one up, so you almost never implement
the bridge yourself. Both are the platform-specific implementation of one contract:

```csharp
public interface INativeWebView
{
    ValueTask ApplyRenderAsync(ReadOnlyMemory<byte> frameUtf8);   // .NET → WebView: push a rendered frame
    ValueTask EvaluateJavaScriptAsync(string javaScript);         // .NET → WebView: IJSRuntime interop
    Func<byte[], Task>? OnMessage { get; set; }                   // WebView → .NET: events / jsResult / …
}
```

`ApplyRenderAsync` hands a frame (UTF-8 JSON) to the WebView's `window.__raskNative.applyRender`.
`OnMessage` is invoked by the platform whenever the page posts back (a component event, a `navigate`, a
`ready` handshake, an IJSRuntime `jsResult`). Both sides speak the same wire format the WASM host uses over
its `Dispatch` boundary. Implement it yourself only for a custom WebView; the shipped heads already do — each
serves a **real origin** (a `WKUrlSchemeHandler` on iOS, `WebViewClient.ShouldInterceptRequest` on Android)
so secure-context device APIs (`localStorage`, `crypto.subtle`) work, and marshals `ApplyRenderAsync`/
`EvaluateJavaScriptAsync` onto the UI thread.

### Why both origins are secure contexts

`crypto.subtle`, `navigator.credentials`, `navigator.locks` and `navigator.storage.estimate` only exist on a
**potentially trustworthy** origin, and an origin that misses that bar doesn't fail loudly — those APIs are
simply `undefined`. Each head clears it a different way, and only one of them is obvious:

| Head | Origin | Secure context because |
|------|--------|------------------------|
| Android | `https://appassets.rask/` | the `https` scheme — the ordinary rule |
| iOS | `raskapp://local/` | WebKit treats a **custom `WKURLSchemeHandler` scheme** as trustworthy |

The iOS row surprises people (`raskapp://` is not `https`, and the host isn't `localhost`, so the
[W3C algorithm][secure-contexts] alone would say no). WebKit goes further than the spec's baseline and
grants scheme-handler origins a secure context regardless of host — verified directly against `WKWebView`:
`raskapp://local/` reports `isSecureContext === true` and a live `crypto.subtle`. The Appium suite asserts
this on device, so a future change to the scheme or origin can't quietly cost you the whole secure-context
API tier.

> If you write your own `INativeWebView`, this is the thing to preserve. Serving the app from `file://` or a
> plain-`http` non-loopback origin would silently drop those APIs.

[secure-contexts]: https://w3c.github.io/webappsec-secure-contexts/#is-origin-trustworthy

## Wiring a platform head

The app head (a `net10.0-ios` / `net10.0-android` project) is just an entry point that composes the shipped
pieces — the WebView bridge, the [native share backend](native-devices.md#native-device-backends), and (Local mode) the host:

```csharp
// Android MainActivity / iOS AppDelegate (Native + Local):
var webView = new RaskAndroidWebView(this);          // or new RaskWkWebView() on iOS
var host = NativeAppHost.CreateDefault();
host.UsePlatform(new AndroidPlatform(this));         // native backends: share, geolocation, clipboard, …
var app = await host.RunLocalAsync<App>(webView);
webView.LoadShell();
```

The heads serve the boot shell + client + your bundled assets through
[`NativeOriginAssets`](#serving-a-full-apps-assets) (below), so there is nothing else to wire. See
`samples/Rask.Example.Native` for a complete head, and the `rask-native` template for a fresh one.

## Serving a full app's assets

The boot shell + client are only two files; a real app also loads **scoped CSS/JS** (`/_rask/a/{hash}.{ext}`),
your `wwwroot` static files, Bootstrap (`/_content/Rask.Bootstrap/*`) and fetches `data/*.json`. `Rask.Native`
ships the request table so your scheme handler is a one-liner — **`NativeOriginAssets.Resolve`**:

```csharp
// In your WebViewClient.ShouldInterceptRequest / WKUrlSchemeHandler:
var path = new Uri(url).AbsolutePath;
if (NativeOriginAssets.Resolve(path, ReadBundledAsset) is { } asset)
    return Respond(asset.Body, asset.ContentType);   // shell/client + scoped assets + your static files
return EmptyOk();                                     // under-origin miss → don't hang the page
```

It resolves the shell/client (`NativeClientAssets`) and scoped assets (`ScopedAssetRegistry`) itself, and
delegates everything else to your `Func<string, byte[]?>` reader — typically the app's **bundled assets**
(Android `AssetManager.Open`, iOS a path under `NSBundle.MainBundle`), so it all works **offline**. For the
in-process demo `HttpClient` (so data-driven pages resolve `data/*.json` off the network too), register it
over **`NativeAssetHttpHandler`** with the same reader and `BaseAddress` = your app origin. See
`samples/Rask.Example.Native` for a complete working head.
