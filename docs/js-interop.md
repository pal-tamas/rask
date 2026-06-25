# JavaScript interop: element refs, scoped CSS & JS

Reaching the DOM and shipping component-scoped styles and scripts. The same code runs on
both transports — Server (WebSocket) and WASM (`JSImport`/`JSExport`).

- [Scoped CSS](#scoped-css)
- [Scoped JS](#scoped-js)
- [Calling JS from C# (`IJSRuntime`)](#calling-js-from-c-ijsruntime)
- [Typed browser APIs](#typed-browser-apis)
- [Element refs](#element-refs)
- [Delivery & caching](#delivery--caching)

---

## Scoped CSS

Drop a `{Component}.css` next to `{Component}.cs` and it is **auto-included** and scoped to
that component — Blazor-parity isolation, no build step:

```
Pages/HomePage.cs
Pages/HomePage.css      ← styles here only apply to HomePage's elements
```

Each component gets a stable `r-{8hex}` scope id. The serializer stamps `data-{scopeId}`
on the component's elements and rewrites every selector to `selector[data-{scopeId}]`.

```css
.card { padding: 1rem; }            /* scoped to this component */
```

`@media` / `@supports` / `@container` / `@layer` recurse into their bodies; `@keyframes`,
`@font-face`, and `@import` pass through unscoped. Opt a project out of auto-globbing with
`<RaskScopedCssAutoInclude>false</RaskScopedCssAutoInclude>`.

**Global styles** (a brand palette, `:root` variables, shell tags like `body`, or framework
classes like Bootstrap's) don't belong in a scoped `{Component}.css` — there is no opt-out
selector. Put them in a plain stylesheet under `wwwroot` and link it from your App
component's `<Head>`, exactly as you would any other static stylesheet:

```csharp
// wwwroot/global.css is a normal, unscoped stylesheet.
Link(Rel: "stylesheet", Href: LiveOptions.PathBase + "/global.css")
```

`LiveOptions.PathBase` keeps the URL correct under a reverse-proxy prefix (Server) or a
sub-path deploy like GitHub Pages (WASM). User `<Head>` contributions are spliced in before
the auto-injected scoped links, so `global.css` sits earlier in the cascade than any scoped
component CSS.

> An orphan `.css` with no matching component, or two that match ambiguously, raises
> **RASK015 / RASK016**. See [diagnostics](diagnostics.md).

---

## Scoped JS

A sibling `{Component}.js` is wrapped onto `window.Rask["{TypeName}"]`, with every
`export function NAME` (or `export async function NAME`) becoming a method:

```js
// ElementRefDemo.js
export function width(el) {
    return el ? el.getBoundingClientRect().width : 0;
}

// async exports work too — e.g. CodeSample.js
export async function copy(text) {
    await navigator.clipboard.writeText(text);
}
```

becomes callable as `Rask.ElementRefDemo.width`. Two scoped-JS components that share a
simple type name collide at `window.Rask[Name]` — **RASK020** warns about this
(RASK017 / RASK018 cover orphan / ambiguous `.js`).

---

## Calling JS from C# (`IJSRuntime`)

Inject `IJSRuntime` through the **constructor** (not a property — a non-nullable settable
property would become a required factory parameter) and dispatch from a lifecycle hook or
event handler:

```csharp
public sealed class CodeSample : Component
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

---

## Typed browser APIs

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
| `IGeolocation` | `navigator.geolocation` | `GetCurrentPositionAsync(GeolocationOptions?)` → `GeolocationPosition` |
| `IPermissions` | `navigator.permissions` | `QueryAsync(PermissionName)` → `PermissionState` |
| `IVibration` | `navigator.vibrate` | `VibrateAsync(params int[])`, `CancelAsync` |
| `IPageVisibility` | `document.visibilityState` | `GetStateAsync()` → `PageVisibility`, `IsHiddenAsync` |
| `INavigatorInfo` | `window.navigator` | `OnLineAsync`, `LanguageAsync`, `UserAgentAsync` |

```csharp
public sealed class ThemeToggle(IBrowserStorage storage, INavigatorInfo navigator) : Component
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

- **`IShare`** (Web Share) needs transient activation, so it is **WASM-only** and lives in
  `Rask.Wasm.Browser` (registered by the WASM host, not `Rask.Core`). On Server `navigator.share`
  would reject with "Must be handling a user gesture," so it isn't offered there.
- **`IClipboard.WriteTextAsync`** needs transient activation *or* a granted `clipboard-write`
  permission — the permission lets it work across the Server round-trip, so it stays shared.
- **`IVibration`** needs only *sticky* activation (the page was interacted with at some point), so it
  works on **both** transports (on devices with a vibration motor).
- Everything else here (storage, cookies, geolocation, permissions, navigator info, page visibility)
  is unaffected by activation and behaves identically on both transports.

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
public sealed class FocusDemo : Component
{
    private readonly IJSRuntime _js;
    private readonly ElementRef _input = ElementRef.New();
    private readonly ElementRef _box = ElementRef.New();

    public FocusDemo(IJSRuntime js) => _js = js;

    protected override RenderResult Render() =>
        Div()[
            Input<string>(InputType.Text, Ref: _input),
            Div(Ref: _box)["measure me"],
            Button(OnClickAsync: Focus)["Focus"],
            Button(OnClickAsync: Measure)["Measure"]
        ];

    // Built-in helpers: ElementRefInterop.{FocusAsync, BlurAsync, ScrollIntoViewAsync}.
    private async Task Focus() => await _input.FocusAsync(_js);

    // Hand the ref to your own scoped JS — it resolves to the element before width() runs.
    private async Task Measure() => await _js.InvokeAsync<double>("Rask.FocusDemo.width", _box);
}
```

Runnable demo:
[`samples/Rask.Example.Shared/Features/ElementRef/ElementRefDemo.cs`](../samples/Rask.Example.Shared/Features/ElementRef/ElementRefDemo.cs)
(+ `ElementRefDemo.js`).

---

## Delivery & caching

Both scoped CSS and JS are **content-addressed**. The generator registers each asset; it
is served at `/_rask/a/{hash}.{ext}` with `Cache-Control: immutable`, an `ETag`,
`nosniff`, and `.AllowAnonymous()`. The page `<head>` emits exactly one `<link>` /
`<script defer>` per mounted component type that has a registered asset. Static-file and
WASM hosts get the same files baked to disk by the `BakeScopedAssetsTask` MSBuild task.

### Eager prefetch

By default the `<head>` *also* emits a low-priority `<link rel="prefetch">` for **every**
registered scoped asset — not just the components on the current route. This warms the
browser's HTTP cache up front, so when a component first mounts later (client-side
navigation, a conditionally rendered section) its stylesheet/script is already downloaded.
Cache-warming alone is not enough to avoid a flash, though — cached *bytes* are not an
*applied* stylesheet. So the live runtime also gates the swap: when a render adds a new
scoped stylesheet it inserts the `<link>` first and holds the body paint until that
`<link>`'s `.sheet` is non-null (the CSSOM stylesheet has parsed and applied), bounded by a
500 ms cap. Together — prefetch (the sheet is warm, so it applies almost instantly) plus the
apply-gate (the body never paints ahead of it) — the body swaps with **no flash of unstyled
content** and the scoped-JS namespace is ready on first interaction. `prefetch` (rather than `preload`) is the future-navigation hint — it
sits at the lowest priority so it never competes with the current route's critical
resources, and it raises no *"resource preloaded but not used"* console warning for the
off-route assets a visitor may never reach. The links are inert (`rel="prefetch"`, neither
a render-blocking stylesheet nor an executable script), and the markup is cached so it
costs a single append per render. Scoped CSS is selector-rewritten to `[data-r-xxxx]`, so
prefetching an unmounted component's styles has no visual effect until its elements exist.

Opt out — to fetch each scoped asset only when its component first mounts (smaller
first-load payload, at the cost of a brief navigation FOUC the first time each new
component type appears) — via `AddRask` / the WASM host builder:

```csharp
builder.Services.AddRask(o => o.PreloadScopedAssets = false); // Server
// or
WasmHostBuilder.CreateDefault(o => o.PreloadScopedAssets = false); // WASM
```

---

See also: [Composition](composition.md) for component-to-component communication, and the
[architecture notes](architecture/live-rendering.md) for how the live runtime ships these.
