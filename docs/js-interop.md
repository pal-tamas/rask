# JavaScript interop: element refs, scoped CSS & JS

Reaching the DOM and shipping component-scoped styles and scripts. The same code runs on
both transports — Server (WebSocket) and WASM (`JSImport`/`JSExport`).

## On this page

- [IJSRuntime, typed APIs & refs](js-interop-runtime.md) — calling JS, the typed browser-API layer, element refs, wrapping a third-party lib.

- [Scoped CSS](#scoped-css)
- [Scoped JS](#scoped-js)
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

Two components declare the **same** `.box` selector in their own `.css`; each is scoped to its own
`data-r-{id}`, so they never collide — one paints red, the other blue:

<!-- demo:js-interop-scoped-css -->

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

## Delivery & caching

Scoped CSS and JS each ship as **one content-addressed bundle**. The generator registers
every component's scoped asset; the framework concatenates all registered scoped CSS into a
single bundle and all registered scoped JS into another (hash-sorted, so the bytes — and the
URL — are deterministic across builds). Each bundle is served at `/_rask/a/{hash}.{ext}` with
`Cache-Control: immutable`, an `ETag`, `nosniff`, and `.AllowAnonymous()` — and **brotli/gzip
compressed** when the client advertises it (negotiated per request, with each compressed
representation built once and cached by content hash since the bytes never change). The page `<head>`
emits exactly **one** `<link rel="stylesheet">` and **one** `<script defer>` — the two
bundles — keyed `rsk-css` / `rsk-js` so the client morph updates them in place when the hash
changes (hot reload). Static-file and WASM hosts get the same two files baked to disk by the
`BakeScopedAssetsTask` MSBuild task, so any static-asset host (`MapStaticAssets`, a CDN)
serves them.

### No navigation FOUC

Because the whole bundle ships up front, a component that mounts *later* — client-side
navigation, a conditionally rendered section — is styled the instant its node is inserted:
its rule is already in the applied CSSOM, so there is no per-component lazy fetch and no flash
of unstyled content, and the scoped-JS namespace (`window.Rask[...]`) is ready on first
interaction. Scoped CSS is selector-rewritten to `[data-r-xxxx]`, so a bundle rule for an
unmounted component has no visual effect until its elements exist.

The demos below all draw from that one shared bundle. A component with only scoped CSS; a component with
scoped JS keeping module state (`window.Rask.JsOnlyDemo.bump`); two components declaring the same
`.twin-tag` selector, each isolated to its own scope; and a lazily-mounted child whose rule already rides
the bundle, so it paints the instant it mounts — no per-component fetch, no FOUC:

<!-- demo:asset-basic-css -->

<!-- demo:asset-js-only -->

<!-- demo:asset-twin-bundle -->

<!-- demo:asset-lazy-mount -->

---

See also: [Composition](composition.md) for component-to-component communication, and the
[architecture notes](architecture/live-rendering.md) for how the live runtime ships these.
