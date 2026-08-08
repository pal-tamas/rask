# Native — device capabilities & chrome

Safe-area insets, the browser-wrapper and native device backends, and the bounded native header/footer chrome.

‹ Back to [Native mobile apps](native.md)

## Safe-area insets (notch / status bar)

The boot shell requests an **edge-to-edge viewport** (`viewport-fit=cover`), so without padding the
UI would render *under* the status bar, notch / Dynamic Island, and home indicator. Rask builds the
document around the root component, so the template's `App.cs` reaches the `<body>` through a `Shell`
override — the padding is a style, which is the one thing `BodyClass` can't carry:

```csharp
protected override Component? Head =>
[
    Title()["Rask App"],
    Meta("utf-8"),
    Meta(Name: "viewport", Content: "width=device-width, initial-scale=1, viewport-fit=cover")
];

// head is the framework's <head> — place it, or the page loses every head asset.
protected override Component Shell(Component head, Component body) =>
    Html("en")[
        head,
        // Pad the body by the device safe-area insets so content clears the status bar / notch /
        // home indicator (the boot shell requests an edge-to-edge viewport with viewport-fit=cover).
        Body(Style: "margin:0;padding:env(safe-area-inset-top) env(safe-area-inset-right) " +
                    "env(safe-area-inset-bottom) env(safe-area-inset-left)")[body]
    ];
```

If you restructure `App.cs`, keep the `viewport-fit=cover` meta and the `env(safe-area-inset-*)`
padding together — dropping either brings content back under the notch.

## Device capabilities

The `IJSRuntime`-backed browser wrappers in `Rask.Core.Browser` (storage, media query, the observers,
crypto, …) work **through the WebView's JS engine** with no extra code — `NativeAppHost` registers them and
`NativeJSRuntime` dispatches them over the bridge. On top of that, `Rask.Native` **ships native C# backends**
for the interfaces where a native API beats the WebView (or the WebView doesn't expose one at all); the
framework wires them ahead of the JS defaults, so you inject the ordinary interface and get the native
implementation. See [Native device backends](#native-device-backends).

## Native device backends

A **native backend** is a C# class that implements a `Rask.Core.Browser` (or `Rask.Client.Browser`)
interface against the platform SDK — `CLLocationManager`, `UIPasteboard`, `ClipboardManager`, and friends —
instead of the WebView's JS. These live in `Rask.Native/Platforms/{iOS,Android}` and compile only for the
head TFMs (the base `net10.0` build stays workload-free). You never register them one by one: a **platform
module** does it, and the framework resolves native-first.

```csharp
// Platforms/iOS/AppDelegate.cs — before RunLocalAsync
host.UsePlatform(new ApplePlatform(() => Window?.RootViewController));

// Platforms/Android/MainActivity.cs — before RunLocalAsync
host.UsePlatform(new AndroidPlatform(this));
```

`ApplePlatform` / `AndroidPlatform` implement `INativePlatform`; `NativeAppHost.RunLocalAsync` invokes them
**before** wiring the JS-backed fallbacks, and every registration uses `TryAdd`. So an interface a platform
backs natively **wins** (native-first), an explicit `host.Services` registration you add yourself wins over
even that, and every interface no one backed falls back to the WebView's JS — the framework picks the best
implementation per interface with no per-API wiring.

The shipped native backends (both platforms):

| Interface | iOS | Android |
|---|---|---|
| `IShare` | `UIActivityViewController` | `Intent.ACTION_SEND` |
| `IGeolocation` | `CLLocationManager` | `LocationManager` |
| `IClipboard` | `UIPasteboard` | `ClipboardManager` |
| `IVibration` | system vibration (AudioToolbox) | `Vibrator` / `VibratorManager` |
| `IWakeLock` | `UIApplication.IdleTimerDisabled` | window `FLAG_KEEP_SCREEN_ON` |
| `INetworkInfo` | `NWPathMonitor` | `ConnectivityManager` |
| `IBattery` | `UIDevice` battery monitoring | `BatteryManager` |
| `ISpeechSynthesis` | `AVSpeechSynthesizer` | `TextToSpeech` |
| `ISpeechRecognition` | `SFSpeechRecognizer` + `AVAudioEngine` | `SpeechRecognizer` |
| `IScreenInfo` | `UIScreen` | `DisplayMetrics` |
| `IDeviceOrientation` | CoreMotion (`CMMotionManager`) | `SensorManager` (rotation vector) |
| `IDeviceMotion` | CoreMotion (`CMMotionManager`) | `SensorManager` (accelerometer + gyroscope) |
| `INotifications` | `UNUserNotificationCenter` | `NotificationManager` (+ channel) |
| `IBadge` | `UNUserNotificationCenter.SetBadgeCount` | badge notification (`setNumber`) |

So `await geolocation.GetCurrentPositionAsync()` returns a native fix (real permission prompt +
`CLLocationManager` / `LocationManager` accuracy) instead of `navigator.geolocation`, `clipboard.WriteTextAsync`
hits `UIPasteboard` / `ClipboardManager` (no WebView gesture gate), `notifications.ShowAsync(...)` raises a real
OS notification where a WebView has no `Notification` API at all, and so on. Some backends need platform
permissions — add `ACCESS_FINE_LOCATION` / `ACCESS_NETWORK_STATE` / `POST_NOTIFICATIONS` (Android) and
`NSLocationWhenInUseUsageDescription` (iOS), and the head requests the location and notification runtime grants.

The **declarative** `Shareable` still reaches the native share sheet through the **capability bridge**: the
native client advertises `window.__raskNative.capabilities` and an `invoke(name, data)` that posts a
`{ type: "capability" }` message; `NativeAppHost` routes it (via `NativeCapabilities.TryHandleAsync`) to the
resolved `IShare` (`invoke("share", …)` → `IShare.ShareAsync`) — so a plain `Shareable` button pops the
native sheet with no host-specific code. The **same** `NativeCapabilities` toolkit lets a **Native + Server**
head inject the bridge into a remote page, so a plain Server app reaches device natives too — see
[Native device APIs from a Server app](native-bridge.md#native-device-apis-from-a-server-app-the-capability-bridge).

**To add your own backend** (or override a shipped one), implement the interface in your head and register it
on `host.Services` before `RunLocalAsync` — it wins over the platform module's version. Further native
backends behind the *same* interfaces (biometrics, native push via APNs/FCM) are a follow-up (see
[Roadmap](native.md#roadmap)).

## Native header & footer

A native page is a small **composed tree**: the native bars (`NativeHeaderBar` / `NativeTabBar` /
`NativeToolbar`) as siblings of a **`NativeWebView`**, which hosts the ordinary page content (the
document around it is the framework's, as on Server and WASM). The native host projects the bars to a
**real `UINavigationBar` + `UITabBar`/`UIToolbar`** on iOS, and a top bar + bottom tab/tool bar on
Android, and serializes the `NativeWebView`'s HTML into the WebView between them. The bars are ordinary
factory-built components — you compose them in `Render()`, they work like any other component:

```csharp
protected override Component? Render() =>
[
    NativeHeaderBar(Title: "Dashboard", Trailing: [NativeBarButton(Icon: NativeIcon.Add, OnClick: OnAdd)]),
    NativeWebView()[Router()],
    NativeTabBar(Tabs: [
        NativeTab(Title: "Home", Icon: NativeIcon.Home, To: Features.Routes.HomePage()),
        NativeTab(Title: "Me",   Icon: NativeIcon.Person, To: Features.Routes.MePage()),
    ], Selected: 0)
];
```

- **`NativeWebView` hosts the HTML** — its children are the page's content; only native bars may sit outside
  it. A bar nested inside the HTML (an element child, or inside `NativeWebView`'s content) is a **RASK032**
  compile error — bars belong at the layout level, as siblings of `NativeWebView`.
- **Type-safe icons** — `NativeIcon` pairs an iOS SF Symbol with an Android drawable/Material name; use a
  curated member (`NativeIcon.Home`) or an escape hatch (`NativeIcon.Custom(sfSymbol, drawable)` /
  `NativeIcon.SfSymbol(...)` / `NativeIcon.Drawable(...)`). Routes are type-safe too (`Features.Routes.*`).
- **Tab badges** — a `NativeTab` takes an optional `Badge` string (an unread count like `"3"` / `"99+"`),
  projected to `UITabBarItem.BadgeValue` (iOS) / a small overlay on the icon (Android). Leave it `null`/empty
  for no badge; bind it to live state (e.g. `Badge: unread.ToString()`) and it updates on the next render.
- **Segmented control** — `NativeHeaderBar` takes optional `Segments` (2–3 short labels) shown in place of the
  title — a `UISegmentedControl` as the nav bar's `titleView` (iOS) / an equivalent row (Android). It is
  controlled: bind `SelectedSegment` to state and handle `OnSegmentChanged(int)` (which runs on the render
  thread and re-renders, like any callback). Use it for a small mode/sub-section switch:
  `NativeHeaderBar(Segments: ["All", "Active", "Done"], SelectedSegment: filter, OnSegmentChanged: i => filter = i)`.
- **Back button** — a `NativeBackButton` in the header's `Leading` slot pops the WebView history (like the
  hardware Back button) — the platform back chevron on iOS, a "‹" on Android. Compose it on a drill-down page
  (e.g. a detail route) to return to the previous screen; the initial route replaces the boot shell URL in
  history so Back from the first navigation lands on the app's first screen, not the shell.
- **Overflow menu** — a `NativeMenuButton` is a bar item (header `Leading`/`Trailing` or a toolbar's `Items`)
  that opens a native pull-down of `NativeMenuItem`s — an iOS `UIMenu` on a `UIBarButtonItem`, an Android
  `PopupMenu` — for secondary actions. It defaults to a "⋯" (ellipsis) icon; each entry has a `Title`, an
  optional `Icon`, an `OnClick`, and an optional `Destructive: true` (iOS renders it in red). Menu selections
  re-enter the ordinary handler path, so `OnClick` runs on the render thread and re-renders:
  `NativeMenuButton(Items: [NativeMenuItem(Title: "Refresh", OnClick: OnRefresh), NativeMenuItem(Title: "Delete", Destructive: true, OnClick: OnDelete)])`.
- **Bar buttons** run their `OnClick` on the render thread and re-render, like any Rask callback. **Tabs**
  navigate to their route; the page recomputes `Selected` from the current route on the next render. Each
  projected bar view carries a stable **accessibility identifier** (the tab/button title, or
  `rask-native-header`), so screen readers — and UI tests like the Appium on-device E2E — can address it.
- **Bars render no HTML** — they are collected during the render walk (so their factories are DI-correct and
  callbacks wire to their owner); the last bar of a kind wins. Only the settled build's chrome is pushed, and
  an unchanged bar never re-pushes (no flicker on a counter tick).
- **Opt-in + inert elsewhere** — register an `INativeChrome` backend on `host.Services` before `RunLocalAsync`
  (the platform WebView heads implement it; assign `webView.ChromeView` instead of `webView.View`), exactly
  like `IShare`. With no backend registered the bars render nothing. Sharing an app across web + native? Branch
  with `IsNative`: compose the native tree under the native shell and return the plain shell on Server/WASM.
  This is a **bounded native-widget surface** (a header + footer), not a general native-control renderer.

### Styling the bars

The HTML inside `NativeWebView` is styled the usual way — scoped CSS, `global.css`, Bootstrap. The **bars**
are real platform views, so they take **native** colors through a small, type-safe surface. `NativeColor` is
the color sibling of `NativeIcon` — one authored value the platform head resolves to a `UIColor` (iOS) /
`Color` (Android):

```csharp
NativeColor.Hex("#1E88E5")                                  // fixed
NativeColor.Rgba(30, 136, 229)                              // fixed, from channels
NativeColor.Adaptive(NativeColor.Black, NativeColor.White)  // light / dark — tracks the system theme
NativeColor.System                                          // the platform default (the unset value)
```

Set colors **per bar** — every slot is optional, and an unset slot keeps the platform default (so styling is
fully opt-in and backward compatible):

```csharp
NativeHeaderBar(Title: "Home",
    Background: NativeColor.Hex("#1E88E5"),
    Tint: NativeColor.White,                                 // leading/trailing button color
    TitleColor: NativeColor.White),
NativeTabBar(Tabs: [...],
    Tint: NativeColor.Hex("#1E88E5"),                        // the selected tab
    UnselectedTint: NativeColor.Hex("#6B7280")),
```

For an app-wide default, register a **`NativeTheme`** on `host.Services` (like `INativeChrome`); a per-bar
color wins, the theme fills the slots a bar left unset, and a slot unset in both keeps the platform default:

```csharp
host.Services.AddSingleton(new NativeTheme
{
    Background = NativeColor.Hex("#1E88E5"),
    Tint       = NativeColor.White,
    TitleColor = NativeColor.White,
});
```

- **Dark mode** — an `Adaptive(light, dark)` color resolves per appearance: on iOS via a dynamic `UIColor`
  (it switches live); on Android against the current night mode (the Activity re-runs on a uiMode change).
- **`NativeColor.System` vs. leaving it null** — omitting a color inherits the theme (then the platform
  default); passing `NativeColor.System` explicitly *overrides* the theme and forces the platform default for
  that one slot.
- **Colors, not CSS** — this is a deliberately small surface (background, tint, title color). Bar fonts,
  heights, and richer Material chrome are out of scope; the palette is kept in C#, so align it with your web
  theme's tokens by hand (the showcase sources both from one `Brand` constant).
