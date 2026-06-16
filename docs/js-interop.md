# JavaScript interop: element refs, scoped CSS & JS

Reaching the DOM and shipping component-scoped styles and scripts. The same code runs on
both transports — Server (WebSocket) and WASM (`JSImport`/`JSExport`).

- [Scoped CSS](#scoped-css)
- [Scoped JS](#scoped-js)
- [Calling JS from C# (`IJSRuntime`)](#calling-js-from-c-ijsruntime)
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
            Input(Type: "text", Ref: _input),
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
