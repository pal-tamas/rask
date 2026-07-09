# Company.RaskNative

A **native iOS + Android** app built with [Rask](https://github.com/pal-tamas/rask). The same C#
component code that runs on the Rask server and in the browser runs here too — packaged as a real,
store-distributable mobile app. It's a **WebView hybrid**: your C# runs natively on the device, and the
UI renders in a platform WebView driven by Rask's live diff pipeline.

## Prerequisites

Install the iOS and/or Android SDK workloads:

```bash
dotnet workload install ios android
```

## Run

```bash
dotnet build -t:Run -f net10.0-android     # Android emulator
dotnet build -t:Run -f net10.0-ios         # iOS simulator (macOS + Xcode)
```

## What's here

- `App.cs`, `HomePage.cs`, `Counter.cs` — your Rask components (shared across both platforms).
- `Platforms/iOS/` — the iOS head: `AppDelegate` boots a `NativeAppHost`, and `RaskWkWebView` implements
  `INativeWebView` over a `WKWebView` (custom `raskapp://` scheme + script-message bridge).
- `Platforms/Android/` — the Android head: `MainActivity` boots the host, and `RaskAndroidWebView`
  implements `INativeWebView` over an `android.webkit.WebView` (asset-serving `WebViewClient` + a
  `@JavascriptInterface` bridge).

Register app services on `host.Services` in the platform head's `StartAsync`, then add pages as
`[Route("/…")]` components — exactly as in any other Rask app.

## Native + Server mode

To make the app a thin native shell over a remote Rask Server instead of running in-process, point the
WebView at the server URL:

```csharp
var shell = NativeAppHost.ConnectToServer(new Uri("https://app.example.com/"));
// iOS:     webView.View.LoadRequest(new NSUrlRequest(new NSUrl(shell.ServerBaseUrl.ToString())));
// Android: webView.View... LoadUrl(shell.ServerBaseUrl.ToString());
```

The server serves its own client and connects back over `wss://`; native device APIs remain available to
the page.

See the framework docs: [Native mobile apps with Rask](https://github.com/pal-tamas/rask/blob/main/docs/native.md).
