# JS interop — IJSRuntime, typed APIs & refs

Calling JS from C#, the typed browser-API layer, element refs, and wrapping a third-party JS library.

‹ Back to [JavaScript interop](js-interop.md)

## Calling JS from C# (`IJSRuntime`)

Inject `IJSRuntime` through the **constructor** (not a property — a non-nullable settable
property would become a required chain step) and dispatch from a lifecycle hook or
event handler:

```csharp
public sealed partial class CodeSample : Component
{
    private readonly IJSRuntime _js;
    public CodeSample(IJSRuntime js) => _js = js;

    protected override async Task OnRenderedAsync(bool firstRender) =>
        await _js.InvokeVoidAsync("Rask.CodeSample.rendered", firstRender);
}
```

Nothing (no `el`) is passed automatically — pass what the function needs. For a return
value use `InvokeAsync<T>`. On WASM a non-primitive `T` must be rooted for the trimmer
(DAM annotation or a `JsonSerializerContext`).

A `sessionStorage` round-trip through the unified `IJSRuntime` — set, read, and remove, each a plain
`InvokeVoidAsync` / `InvokeAsync<string?>` against a built-in browser API, identical on both transports:

<!-- demo:js-interop-jsruntime -->

---

## Typed browser APIs

> For the **full map** of every wrapper (shared vs WASM-only, one-shot vs subscription), see the
> [Browser APIs overview](browser-apis.md). This section covers the shared set and the transport "why".

Rather than spelling out raw `IJSRuntime` identifiers (`"localStorage.getItem"`,
`"navigator.clipboard.writeText"`) and getting the JSON shape right by hand, inject one of the
built-in **typed wrappers** through a component constructor. Each is a thin, awaitable layer over
the same unified `IJSRuntime`, so it behaves **identically on Server and WASM**. These are the
Web APIs that work on both transports; WASM-only PWA APIs (service worker, cache, manifest) are a
later step on the same pattern.

| Service | Wraps | Key members |
| --- | --- | --- |
| `IBrowserStorage` | `localStorage` / `sessionStorage` | `.Local` / `.Session` → `GetAsync`, `SetAsync`, `RemoveAsync`, `ClearAsync`, `KeyAsync`, `LengthAsync` |
| `ICookies` | `document.cookie` | `GetAsync`, `SetAsync(name, value, CookieOptions?)`, `DeleteAsync`, `GetAllAsync` |
| `IClipboard` | `navigator.clipboard` | `WriteTextAsync`, `ReadTextAsync` |
| `IGeolocation` | `navigator.geolocation` | `GetCurrentPositionAsync(GeolocationOptions?)` → `GeolocationPosition`; `WatchAsync(Func<GeolocationPosition,Task>, …)` → `IAsyncDisposable` (live tracking) |
| `IPermissions` | `navigator.permissions` | `QueryAsync(PermissionName)` → `PermissionState` |
| `IVibration` | `navigator.vibrate` | `VibrateAsync(params int[])`, `CancelAsync` |
| `IPageVisibility` | `document.visibilityState` | `GetStateAsync()` → `PageVisibility`, `IsHiddenAsync` |
| `INavigatorInfo` | `window.navigator` | `OnLineAsync`, `LanguageAsync`, `UserAgentAsync` |
| `INetworkInfo` | `navigator.connection` | `IsSupportedAsync`, `GetStatusAsync()` → `NetworkStatus?` (effective type, downlink, RTT, Data Saver) |
| `IMediaQuery` | `window.matchMedia` | `MatchesAsync(query)`, `PrefersDarkAsync`, `PrefersReducedMotionAsync` |
| `ISpeechSynthesis` | `window.speechSynthesis` | `IsSupportedAsync`, `SpeakAsync(text, SpeechOptions?)`, `CancelAsync` |
| `IScreenInfo` | `window.screen` | `GetAsync()` → `ScreenInfo` (width/height, avail size, color depth, device pixel ratio) |
| `IStorageEstimator` | `navigator.storage.estimate` | `IsSupportedAsync`, `EstimateAsync()` → `StorageEstimate?` (quota / usage bytes + `UsageRatio`) |
| `IVisualViewport` | `window.visualViewport` | `IsSupportedAsync`, `GetAsync()` → `VisualViewport?` (visible size/offset/zoom after the soft keyboard) |
| `IBroadcastChannel` | `BroadcastChannel` | `OpenAsync(name, Func<string,Task>)` → connection (`PostAsync`, `IAsyncDisposable`) — cross-tab messaging |
| `IIntersectionObserver` | `IntersectionObserver` | `ObserveAsync(ElementRef, Func<IntersectionEntry,Task>, IntersectionOptions?)` → `IAsyncDisposable` — element enters/leaves the viewport |
| `IResizeObserver` | `ResizeObserver` | `ObserveAsync(ElementRef, Func<ResizeEntry,Task>)` → `IAsyncDisposable` — element's size changes |
| `IMutationObserver` | `MutationObserver` | `ObserveAsync(ElementRef, Func<MutationEntry,Task>, MutationOptions?)` → `IAsyncDisposable` — element's children/attributes/text change |
| `IMediaSession` | `navigator.mediaSession` | `SetMetadataAsync`/`SetPlaybackStateAsync` + `SetActionHandlerAsync(MediaSessionAction, Func<Task>)` → `IAsyncDisposable` — now-playing metadata + media keys |
| `IDeviceOrientation` | `deviceorientation` | `RequestPermissionAsync()` + `WatchAsync(Func<OrientationReading,Task>)` → `IAsyncDisposable` — gyroscope/compass tilt |
| `IDeviceMotion` | `devicemotion` | `RequestPermissionAsync()` + `WatchAsync(Func<MotionReading,Task>)` → `IAsyncDisposable` — accelerometer / rotation |
| `ICrypto` | `crypto` / `crypto.subtle` | `RandomUuidAsync`, `RandomBytesAsync(length)`, `DigestHexAsync(HashAlgorithm, text)` |
| `IPerformance` | `performance` | `NowAsync()` (high-res clock), `GetNavigationTimingAsync()` → `NavigationTiming?` (TTFB / DCL / load) |
| `IIndexedDb` | `IndexedDB` | `IsSupportedAsync`, `OpenStoreAsync(name)` → `IKeyValueStore` (`Set`/`Get`/`SetBytes`/`GetBytes`/`Delete`/`Keys`/`Clear`) — large async persistent storage, text or raw bytes |

```csharp
public sealed partial class ThemeToggle(IBrowserStorage storage, INavigatorInfo navigator) : Component
{
    private async Task Save() => await storage.Local.SetAsync("theme", "dark");

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (firstRender)
        {
            var theme = await storage.Local.GetAsync("theme");   // string?, null if absent
            var online = await navigator.OnLineAsync();          // bool
        }
    }
}
```

Call them from an **event handler or lifecycle hook** (not from `Render()`). Clipboard and
geolocation are **browser-gated** — they need a secure context (HTTPS or localhost) and the user's
permission; a denial or timeout surfaces as a `JSException` from the awaited task, so wrap those
calls in `try/catch`.

**User-activation and the transport — why one API is WASM-only.** Some browser APIs require
*transient* activation: they must run inside the live user-gesture task. On **WASM** an event
handler's interop call runs synchronously in that gesture's call stack, so it qualifies; on **Server**
the click is forwarded over the WebSocket and the interop call runs a round-trip later, after the
transient activation has expired. The practical effect:

- **Sharing** splits by *when* you fire it. The headless declarative **`Shareable`** (`Rask.Core`) attaches
  `data-rask-share` to your element and the shared client fires `navigator.share` **inside the click's own
  call stack**, so the activation is still live — it therefore works on **every** host, Server included. The
  imperative **`IShare`** (`Rask.Client.Browser`) lets you
  share from *code* (a lifecycle hook, after an `await`), which needs the in-process transport to keep the
  activation — so it's registered only by the **WASM and Native** hosts (on Server `navigator.share` would
  reject with "Must be handling a user gesture"). `Rask.Native` can't reference the browser-targeted
  `Rask.Wasm`, so the WASM+Native-shared `IShare` lives in `Rask.Client`; on Native a platform head can
  register a native backend (`UIActivityViewController` / `Intent.ACTION_SEND`) that needs no activation —
  see the [Native guide](native-devices.md#native-device-backends).
- **`IBadge`** (app icon badge), **`IWakeLock`** (keep the screen awake), **`IScreenOrientation`**
  (read/lock orientation), **`IFullscreen`** (present an element/page fullscreen — like `IShare`,
  `requestFullscreen` needs transient activation), and **`IInstallPrompt`** (capture/replay the deferred
  `beforeinstallprompt` for a custom install button) are likewise **WASM-only** in `Rask.Wasm.Browser` —
  they depend on the installed-PWA instance or the live document the Server round-trip can't carry. See
  the [Mobile & PWA guide](pwa.md#device-capabilities-for-mobile).
- **`IClipboard.WriteTextAsync`** needs transient activation *or* a granted `clipboard-write`
  permission — the permission lets it work across the Server round-trip, so it stays shared.
- **`IVibration`** needs only *sticky* activation (the page was interacted with at some point), so it
  works on **both** transports (on devices with a vibration motor).
- Everything else here (storage, cookies, geolocation, permissions, navigator info, network info, media
  queries, speech synthesis, screen info, storage estimate, visual viewport, broadcast channel, crypto,
  performance, indexeddb, page visibility) is unaffected by activation and behaves identically on both
  transports.

Most of these are one-shot request/response calls. **`IBroadcastChannel`**, **`IIntersectionObserver`**,
**`IResizeObserver`**, **`IMutationObserver`**, **`IDeviceOrientation`**, **`IDeviceMotion`**, **`IMediaSession`**'s
action handlers, and **`IGeolocation.WatchAsync`** are the exceptions — they're *subscriptions*: you
open/observe/watch (returning an `IAsyncDisposable`) and the browser **pushes** each change back to a C#
handler (via a static `[JSInvokable]`, so one wiring works on both transports — the observers additionally
hand the observed element across as an `ElementRef`). Open from a lifecycle hook and dispose on unmount; a
handler
that updates state calls `StateHasChanged()` — the same pattern as subscribing to a background feed (it's a
subscription, not a render/binding callback, so RASK026 doesn't apply).

This is the rule for the whole surface: **shared APIs live in `Rask.Core.Browser`; APIs that can't
work on Server live in `Rask.Wasm.Browser`** (the home for upcoming PWA-only APIs too).

Under the hood: storage/clipboard methods are plain function calls; `navigator.onLine` and
`localStorage.length` are *property* reads the client returns directly; and the callback-based
`getCurrentPosition` is wrapped in a Promise by the framework helper `__raskApi.geolocation`. That
helper (and `__raskEl`) lives in `src/Rask.Core/Resources/rask-api.js` and is spliced into both
client runtimes at build time, so the two transports never drift. `GeolocationPosition` is rooted
for the WASM trimmer by the framework, so it deserializes correctly in a `PublishTrimmed` app.

Runnable demos: the **Browser APIs** section of the showcase — one page per wrapper under
[`samples/Rask.Example.Shared/Features/Browser/`](../samples/Rask.Example.Shared/Features/Browser/)
(e.g. `StorageDemo.cs`, `CookiesDemo.cs`, `PermissionsDemo.cs`, `ShareDemo.cs`).

---

## Element refs

Every element exposes a `Ref:` parameter. Mint a ref with `ElementRef.New()` and store it
in a **field** (so its id is stable across renders), then hand it to JS — it serializes as
`{"__raskRef__":"id"}` and both clients revive it to the live DOM element before your
function runs:

```csharp
public sealed partial class FocusDemo : Component
{
    private readonly IJSRuntime _js;
    private readonly ElementRef _input = ElementRef.New();
    private readonly ElementRef _box = ElementRef.New();

    public FocusDemo(IJSRuntime js) => _js = js;

    protected override Component? Render() =>
        Div[
            Input<string>().Type(InputType.Text).Ref(_input),
            Div.Ref(_box)["measure me"],
            Button.OnClickAsync(Focus)["Focus"],
            Button.OnClickAsync(Measure)["Measure"]
        ];

    // Built-in helpers: ElementRefInterop.{FocusAsync, BlurAsync, ScrollIntoViewAsync}.
    private async Task Focus() => await _input.FocusAsync(_js);

    // Hand the ref to your own scoped JS — it resolves to the element before width() runs.
    private async Task Measure() => await _js.InvokeAsync<double>("Rask.FocusDemo.width", _box);
}
```

Focus a built-in element, then hand a ref to a sibling `.js` that measures it — the ref revives to the
live DOM node before the function runs:

<!-- demo:js-interop-elementref -->

---

## Wrapping a third-party JS library

Everything above is enough to wrap a library that owns its own DOM — a chart, a code editor, a map. Two
questions come up every time: **what happens to the DOM the library builds**, and **what happens to the
`<style>` it injects**. Rask answers the second for you; the first is one rule.

### Give the library a leaf to own

Render the host element with **no children** and let the library fill it. That's the whole rule, and it
works because of how the diff addresses nodes: ops are computed from your C# render tree and applied by
positional path, so a node your components never render is a node the diff can never reach.

```csharp
private readonly ElementRef _host = ElementRef.New();   // a field — the id must be stable across renders

// A leaf: no children here, ever. The library owns everything inside it.
protected override Component? Render() => Div.Ref(_host).Class("chart");

// Mount in OnRendered, not OnMount — OnMount runs *before* the first render, so the element doesn't
// exist yet and the ref would resolve to null. firstRender guards against re-mounting.
protected override async Task OnRenderedAsync(bool firstRender)
{
    if (!firstRender) return;
    await _js.InvokeVoidAsync("Rask.Chart.mount", _host, DataAsJson());
}

// Fires only on a real prop change — push new data at the library instead of re-mounting it.
protected override async Task OnPropsChangedAsync() => await _js.InvokeVoidAsync("Rask.Chart.update", _host, DataAsJson());

// Sync and fire-and-forget — see the note below on why this must not be an awaited DisposeAsync.
protected override void OnUnmount() => _ = DestroyQuietlyAsync();
```

There is one exception to "the diff can't reach it", and it is not optional. Not every frame is a diff:
the first interactive frame after page load always ships the body in full, and a structural change can
too. The client applies a full frame by **morphing** the document, and a morph pairs each live child
against the rendered one — your host has live children where the render says none, so the morph clears
it. Skip that and the chart is deleted seconds after it draws. Tag the library's nodes; the reconciler
leaves marked nodes alone:

```js
// Right after the library builds its DOM. Mark what it created — never the host itself, which your
// component *does* render (marking that makes the morph treat it as missing and append a duplicate).
for (const child of host.children) child.setAttribute("data-rask-managed", "");
```

One more identity rule, because it bites stateful wrappers specifically: a component's identity is its
**(type, position)** among its parent's children. A sibling rendered as `cond ? node : null` shifts every
later child's position when it vanishes, so the wrapper gets matched against the wrong slot and rebuilt —
remounting the widget on an unrelated click. Prefer disabling to un-rendering a sibling above a
stateful component.

For events coming back the other way, a library callback can't reach an instance method — the JS shim
dispatches to **static** `[JSInvokable]`s by assembly and name. Hand JS a token at mount, keep a static
`ConcurrentDictionary<string, YourComponent>`, route on it, and unregister on unmount. Two things to get
right, because a `[JSInvokable]` is callable by *any* script on the page with *any* arguments:

- **Make the token unguessable** (`Guid.NewGuid().ToString("N")`). That dictionary is static, so on the
  Server host it is shared by every live session — with a sequential `int`, one visitor could drive
  another's widget by counting from 1. Holding the token is what proves ownership.
- **Unregister on unmount**, or the entry pins the component for the life of the process.

Keep the boundary to primitives and JSON strings and a trimmed WASM publish stays clean. Prefer callbacks
that take **one** argument (bundle extras into a record): the generated chain step only wraps arity-≤1
delegates for auto-re-render, so a two-arg callback silently leaves the caller reaching for
`StateHasChanged()`.

Finally, tear down from `OnUnmount` **without awaiting** the interop call. An `IAsyncDisposable`
component is awaited by the framework's dispose walk, and that walk also runs for a session whose socket
has already closed — where an interop call has nobody to answer it and never completes.

A Gantt chart wrapping [frappe-gantt](https://github.com/frappe/gantt), start to finish — drag or resize
a bar and the C# table below it updates; add or remove one and the chart follows. That it is on screen at
all is the marker above doing its job:

<!-- demo:js-interop-thirdparty -->

> A scoped `{Component}.css` **cannot** style the library's internals: scoping works by stamping
> `data-{scopeId}` on the elements your component renders, and the library's nodes never get it. Size the
> host in scoped CSS; let the library's own stylesheet handle the rest.

### What it injects into `<head>`

Rask treats `<head>` as **authoritative**: on every re-render the live-diff reconciler morphs the live
head back to what your components rendered, which keeps `<title>`/`<meta>`/scoped-CSS links correct. A
`<style>`/`<link>`/`<script>` that a **JS library injects into `<head>` at runtime** (a code editor's
theme colours, a charting library, a syntax highlighter, an analytics tag) isn't part of that render —
so **Rask preserves it for you automatically**. The reconciler watches `<head>` and tags anything a
library injects with `data-rask-managed` (the same marker it uses for its own scoped-asset tags), so it
survives every re-render with **no code on your side**. The [playground](playground.md) relies on this to
keep Monaco's editor theme across each Run.

The mechanism only preserves nodes injected *after* an initial render (the common case — libraries set up
once your component has mounted). If you need to keep something present at first paint, or want to be
explicit, mark it yourself — the reconciler never touches a head child carrying `data-rask-managed`:

```js
// You rarely need this — runtime-injected head nodes are preserved automatically. Use it only to opt a
// node out explicitly (e.g. one present before the app's first render).
styleEl.setAttribute("data-rask-managed", "");
```
