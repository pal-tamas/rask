# Changelog

All notable changes to Rask are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions are stamped at pack
time (`$(PackageVersion)`); this log groups changes by the pull request that introduced
them until tagged releases begin.

## [Unreleased]

### Added
- **Accessibility primitives on every element.** A new `Aria` parameter — a `string → string?`
  dictionary modelled on the existing `Data` (`data-*`) bag — renders each entry as
  `aria-{key}="{value}"`, so the full WAI-ARIA vocabulary is reachable without a typed property per
  attribute (`Button(Aria: new() { ["label"] = "Close" })` → `aria-label="Close"`). `Role` (`string?`)
  and `TabIndex` (`int?`) are typed parameters for the two non-`aria-*` affordances. The universal
  attribute order is now **id, class, style, data-\*, role, tabindex, aria-\*, then tag-specific**. A
  new **RASK023** analyzer warns when an `Img` is created without `Alt` (pass `Alt: ""` for decorative
  images). Demonstrated on the `/props` showcase page. See the new [accessibility guide](docs/accessibility.md).
- **`TableModel<T>` — a headless, fully *controlled* table primitive** (in the spirit of TanStack
  Table). It renders no markup of its own and owns no state: the host sorts, filters, and pages its
  own data and hands the model the final `Rows` plus the current view state (`Sort`, `PageIndex`,
  `SelectedKeys`, …). The model projects sort-aware `Headers` and selection-aware `Rows` into the
  `Render` delegate and raises `OnSort` / `OnPage` / `OnSelect` **intents**; the host applies them to
  its own state and re-renders. Supporting types live in the new `Rask.Core.Tables` namespace
  (`ColumnDef<T>`, `ColumnSort`, `SortDirection`, `HeaderCell`, `TableRow<T>`, `TableModelContext<T>`).
  The `/table` showcase page now drives its sorting and paging through `TableModel<T>` from the URL
  query string.
- **Master-detail showcase page (`/master-detail`)** — a datagrid with collapsible rows whose expanded
  panel hosts a second, independently sortable `TableModel<T>`. Expand and both grids' sort are held in
  plain component fields; each open row inserts a keyed detail `<tr>`, so the live diff reconciles
  expand/collapse as an in-place keyed insert/remove and sibling open rows keep their own inner sort.

### Changed
- **The live-diff codec is now a single shared client source.** `applyDiff` and its helpers
  (`resolvePath`, `relevantChild`/`relevantChildSkipping`, `moveChildBefore`, `syncFormProperty`)
  were duplicated between the Server (`rask.js`) and WASM (`rask.wasm.js`) clients; they now live
  once in `src/Rask.Core/Resources/rask-dom.js`, spliced into both at build time at a new `RASK_DOM`
  marker — the same mechanism the full-HTML morph (`rask-morph.js`) already used. Internal only: no
  API or behaviour change beyond the fix noted below. Both runtimes now decode the C# `FrameDiffer`
  opcodes from one source and cannot drift.
- **Every showcase example page now shows its own source via `CodeSample`.** The five remaining
  pages without a runnable-source panel were brought into the convention: `/asset-loading` (each
  scoped-asset section is now a `CodeSample` with its component source + live result), `/jsruntime`
  (the `sessionStorage` round-trip was extracted into a `JsRuntimeDemo` shown beside its source),
  and `/master-detail`, `/table`, `/todos` (which append a `CodeSample` of the page's own source
  beneath the live demo). The `[NotFound]` page and the routing-demo sub-page are intentionally
  excluded.

### Fixed
- **Server mode now executes scripts inserted through a keyed diff.** A scoped
  `<script src="/_rask/a/{hash}.js">` (or a user `Head` `<script>`) delivered via a keyed
  `InsertSubtree` diff was parsed by the Server client but never ran — `innerHTML`-parsed scripts
  carry the "already started" flag — so its `window.Rask.{Type}` global never appeared. The shared
  codec now revives inserted scripts via `reviveScript`, matching the full-HTML morph path (the WASM
  client already did this). Only the Server diff path is affected.
- **The `/virtualize` showcase table header no longer disappears while scrolling.** The off-screen
  rows were reserved by two spacer `<div>`s *outside* the table, so the table's own box was relaid
  out on every scroll frame and the sticky `<thead>` unstuck — the header vanished mid-scroll and
  snapped back when scrolling stopped. The spacers are now two keyed spacer **rows inside the
  `<tbody>`**, making the single table the scroller's only child with a constant outer height, so
  the header's containing block never resizes. Stickiness also moved onto the `<th>` cells (opaque
  background + inset `box-shadow` divider, `border-collapse:separate`). Verified in a headless
  browser: the header stays pinned to the scroller top across a 40-step rapid scroll burst. The
  page also now presents both demos through the `CodeSample` component so the runnable source is
  shown beside the live result.
- **Clean parallel builds of WASM-hosted apps no longer race the static-web-assets pipeline.** A full
  rebuild (`dotnet build --no-incremental`, or Rider's *Rebuild Solution*) could fail with `MSB4018`
  from `UpdateExternallyDefinedStaticWebAssets` — the ASP.NET host project resolved the WASM
  project's fingerprinted `dotnet.native.*` assets before that project had emitted them. A Rask WASM
  host serves the published bundle from the publish directory at runtime (`app.UseRask()`) and owns no
  static web assets of its own, so the hosts (and the `rask-wasm-hosted` template) now set
  `StaticWebAssetsEnabled=false`, which skips the racy cross-project resolution while preserving build
  ordering. Downstream hosts that serve only a Rask WASM bundle should do the same.

### Changed
- **Renamed the headless `Virtualize<T>` primitive to `VirtualizeModel<T>`** (component + factory) so
  the headless "view-model" primitives read as one family with `TableModel<T>`. **Breaking:** update
  call sites from `Virtualize<T>(…)` to `VirtualizeModel<T>(…)`; behaviour is unchanged. The
  `Rask.Core.Virtualization` support types keep their names.

## [0.9.0] - 2026-06-16

### Added
- **Eager prefetch of scoped CSS/JS eliminates navigation FOUC.** The page `<head>` now also
  emits a low-priority `<link rel="prefetch">` for *every* registered scoped asset — not just the
  components on the current route — so when a component first mounts later (client-side navigation,
  a conditionally rendered section) its stylesheet/script is already in the browser cache and the
  scoped-JS namespace is ready on first interaction. (Cache-warming alone does not remove the
  flash — the live runtime also holds the body paint until the sheet has *applied*; see the
  matching **Fixed** entry below.) `prefetch` (rather than `preload`) keeps these hints at the lowest priority and
  avoids a *"resource preloaded but not used"* console warning for off-route assets. The markup is
  render-independent and cached (rebuilt only when the asset set changes), so it costs a single
  append per render. On by default; opt out with `AddRask(o => o.PreloadScopedAssets = false)` (or
  the equivalent WASM host-builder option) to fetch each scoped asset only when its component first
  mounts.

### Fixed
- **Scoped-asset flicker on client-side navigation and lazy mount is gone — the live runtime now
  holds the body paint until a newly mounted component's scoped stylesheet has *applied*.** The
  eager `<link rel="prefetch">` warms the HTTP cache, but cached bytes are not an applied
  stylesheet: the client previously swapped in the styled body in the same morph that inserted the
  `<link>`, so it painted unstyled for a frame while the (cached) CSS parsed. The render-application
  path now gates on the `<link>`'s `.sheet` property — non-null only once the CSSOM stylesheet is
  parsed and applied — rather than on Resource Timing `responseEnd` (which a prefetch satisfies the
  moment the bytes land, before the rule applies). A full reply preloads each new scoped stylesheet
  and awaits its `.sheet` before the morph paints the body; a diff that morphs a `<head>` fragment
  waits the same way. Both bound the wait by a 500 ms cap, so a genuinely slow/failed sheet shows a
  briefly unstyled page rather than stalling navigation. The scoped-JS invoke gate is hardened the
  same way on WASM: a prefetched scoped `<script>` now waits for its real `load` event before a
  first-render `Rask.*` invoke dispatches, so the call can't race ahead of the script's execution.
- **Switching a syntax-highlighted code-sample tab no longer renders the new pane's markup as
  literal text.** A live re-render that replaced one `Raw` value with another (e.g. switching a
  `CodeSample` tab from highlighted C# to highlighted CSS) shipped an in-place `UpdateText` diff op,
  which the client applied via `textContent` — escaping the token `<span>` markup into visible
  `<span class="cssSelector">…` text and only touching the first of the `Raw`'s several DOM nodes.
  A `Raw` value change is now treated as a structural replace that routes through the full-HTML
  morph, so the browser reparses the new markup into real token spans. Unchanged `Raw` values still
  diff to zero ops; `Text` nodes are unaffected.

### Changed
- **Showcase & auth samples reorganized into feature folders.** `samples/Rask.Example.Shared`
  moved from flat `Pages/` + `Demos/` + `Layout/` to per-feature folders under `Features/` with
  cross-cutting infrastructure (`CodeSample`, `EmbeddedSource`, `PageHeader`, `ShowcaseLayout`, …)
  in `Shared/`; the four `Rask.Example.Auth*` apps likewise moved to `Features/` + `Shared/`,
  matching `Rask.Example.EfCore`. No public framework API changed and all routes are unchanged.
- **Every showcase code sample now displays its real, full component class verbatim.** Each
  `CodeSample` reads its source through `EmbeddedSource.Read(...)` instead of a hand-written string
  literal, so the shown code is the actual compiled class and can never drift from the live result.
  Inline demos were promoted to dedicated, self-contained component classes co-located with their
  feature. A build-time check now fails if two embedded source files share a leaf name.
- **Showcase code samples now show one tab per source file, labelled with its file name.**
  `CodeSample` takes an ordered list of embedded file names and renders a tab per file (the
  highlight language is inferred from the extension), replacing the previous generic `C#` / `JS` /
  `CSS` strip that concatenated sibling files of the same language into a single pane. Multi-file
  demos (e.g. Scoped CSS, Context, Background service) now expose each file on its own tab.

## [0.8.0] - 2026-06-15

### Added
- **Background-service showcase (`/background`).** A new example page demonstrating an app-wide
  background process driving the UI: a DI **singleton** `IMetricsFeed` runs its own loop and raises
  an event each tick, and two independent components (`MetricsGauge`, `MetricsChart`) subscribe via
  `feed.Updated += StateHasChanged` in `OnMount` / `-=` in `OnUnmount`. Unlike the existing Live
  ticker — whose poll loop lives inside one component — this producer is decoupled from the
  component tree (it keeps ticking across navigations and, on the Server, across sessions). State is
  published as a single immutable snapshot swapped by reference so cross-thread reads are consistent.
  Runs in both the Server and WASM sample apps. `Sparkline` gains an optional `ValueFormat` so its
  axis labels can render as percentages (default unchanged).
- **Slow-connection affordances on both transports.** WASM boot now renders a determinate
  download-progress bar on the splash (driven by the runtime's `onDownloadResourceProgress`
  resource count, not bytes — so Brotli/gzip-precompressed assets don't skew it), replacing the
  indefinite spinner so a slow link shows movement instead of an apparent hang; it falls back to
  the spinner when the total is unknown. Server mode adds a subtle top-of-viewport **pending-action
  bar** that appears when a handler round-trip outlives a ~300ms latency threshold and clears when
  the reply lands — so a high-latency user sees their click registered. It is backed by an opt-in
  WebSocket ack: a client tags handler events with a monotonic `seq` and the server replies
  `{type:"ack",seq}` once the dispatch completes, **even when the render dedupes and ships no
  frame** (otherwise a no-op click would wedge the bar). Seq-less clients are unaffected — the prior
  frame contract is byte-for-byte unchanged.

### Changed
- **`Authorize`'s `Authorized` slot now receives the current user** — its type changed from `Child` to
  `Func<ClaimsPrincipal, Child>`, so authorized markup can greet the signed-in user
  (`Authorized: user => H1()[$"Hi {user.Identity!.Name}"]`, the headless analogue of Blazor's
  `AuthorizeView` `@context.User`) without injecting `IUserProvider` or subscribing to
  `IUserProvider.Changed` — the delegate re-runs with the fresh principal whenever the gate
  re-renders. **Breaking:** static authorized content moves to the children-indexer shorthand —
  `Authorize(Authorized: Panel())` becomes `Authorize()[ Panel() ]`. `NotAuthorized`/`Authorizing`
  are unchanged.
- **WASM build migrated to `Microsoft.NET.Sdk.WebAssembly`** so framework assets are
  content-fingerprinted (`dotnet.<hash>.js`, `App.<hash>.wasm`) and `index.html` carries an
  SDK-generated import map with integrity hashes. This fixes the GitHub Pages (and any static
  host) failure where a redeploy paired a stale, browser-cached integrity manifest with freshly
  served assemblies and tripped a subresource-integrity error on every `_framework/*.wasm` until
  the cache expired — fingerprinted URLs change per release, so a stale asset can never collide
  with a new manifest. **Breaking for downstream WASM consumers:** set
  `<Project Sdk="Microsoft.NET.Sdk.WebAssembly">`, replace `<WasmGenerateAppBundle>true` with
  `<RaskWasm>true</RaskWasm>` + `<OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>`,
  and add a `wwwroot/index.html` shell (the `dotnet new rask-wasm*` templates already include
  one). Published output moves from `bin/<cfg>/net10.0-browser/browser-wasm/AppBundle/` to
  `bin/<cfg>/net10.0-browser/publish/wwwroot/`; `Rask.Wasm.Hosting`'s `UseRask()` follows it
  automatically. Sub-path deploys keep `/p:RaskPathBase=/<repo>` (now a post-publish `<base href>`
  rewrite; every other asset URL is document-relative).
- **Showcase code samples are now copy-pasteable and multi-language.** Every `CodeSample` card in
  the example apps gains a copy-to-clipboard button (scoped JS, with an `execCommand` fallback for
  non-secure/headless contexts), and samples that have a JavaScript or CSS side render a **C# / JS /
  CSS tab strip** — tab state is component state switched through the live diff, and each pane is
  highlighted server-side by ColorCode. The Element-refs and Scoped-CSS pages now show the **real,
  verbatim source** of their demo components (`ElementRefDemo.cs` + `.js`, `ScopedRed/Blue.cs` +
  `.css`), embedded from the actual files via a new `EmbeddedSource` reader so the snippet always
  compiles and matches the live result. Samples-only — no framework API change.

### Removed
- **`:global(...)` scoped-CSS escape hatch removed.** A scoped `{Component}.css` no longer has a
  per-selector opt-out. Global styles — brand palettes, `:root` variables, shell tags (`body`/`html`),
  and framework classes like Bootstrap's — now belong in a plain `wwwroot/global.css` linked from your
  App component's `<Head>` (`Link(Rel: "stylesheet", Href: LiveOptions.PathBase + "/global.css")`),
  exactly like any other static stylesheet; user `<Head>` contributions already load before the
  framework's scoped links. This drops the special-case selector rewriting and removes a source of
  confusion about scoping semantics. **Breaking:** move `:global(...)` rules out of `{Component}.css`
  files into a `wwwroot` stylesheet — all sample apps have been migrated as the worked example. See
  [scoped CSS](docs/js-interop.md).

### Documentation
- EF Core data-access guide: clarified that an awaited event handler re-renders automatically on
  completion (like an async lifecycle hook), and removed the misleading explicit `StateHasChanged()`
  from the delete example — it's only needed for out-of-band changes (timers, fire-and-forget,
  external event subscriptions). The `Rask.Example.EfCore` sample's `DeleteAsync` drops the call.

### Fixed
- **Scoped JS now supports `export async function`.** The module wrapper that exposes a sibling
  `{Component}.js` on `window.Rask["{Type}"]` only recognised `export function` / `export const`,
  so an `export async function name(...)` kept its `export` keyword inside the (non-module) IIFE —
  a `SyntaxError` that silently prevented the *entire* file from loading, leaving `Rask.{Type}`
  undefined and every invoke into it failing with "Could not find … on target". The export-strip
  and export-collection now accept the optional `async` modifier (sync and async exports may be
  mixed in one file). Affects both the Server runtime and the WASM publish bake (both go through
  the same `ScopedAssetRegistry` wrapping).
- **Navigation now scrolls to the top of the new page.** Forward client-side navigation (a `NavLink`
  click or `Navigator.Navigate`) previously left the window at the previous page's scroll position, so
  users could land mid-page on a new route. It now resets the scroll to the top on a history *push*,
  matching a server-rendered page load. Back/Forward and in-place URL changes (`SetQuery`, auth
  redirects — history *replace*) are left to the browser's native scroll restoration. When a
  `NavLink`'s `Href` carries a `#fragment` that matches an element on the destination page, the runtime
  scrolls to that element (and preserves the fragment in the address bar) instead of jumping to the
  top. Fixed in the shared client runtime, so both the Server (WebSocket) and WASM transports get it.
- **Showcase side navigation no longer overflows the page.** On desktop the long sidebar link list
  was an unconstrained `position-sticky` block, so when it exceeded the viewport its bottom entries
  fell below the fold and were only reachable by scrolling the whole page. The sidebar is now pinned
  under the navbar, capped at the available viewport height, and scrolls on its own. The navbar
  height — previously a `56px` literal duplicated across the sidebar `min-height`, the mobile-drawer
  offset, and the sticky `top` — is centralised in a single `--nav-h` custom property (samples only).
- **`Rask.Wasm.Hosting` now serves precompressed static assets with their real MIME type.** When a
  request for a `wwwroot` file (e.g. `global.css`) was satisfied from its publish-time `.br`/`.gz`
  sibling, `UseStaticFiles` keyed the content type off the `.br`/`.gz` extension and fell back to
  `application/octet-stream` — which browsers refuse to apply as a stylesheet (or execute as a
  module), so a linked `global.css` silently had no effect on a hosted WASM app. The host now
  re-derives the type from the underlying asset name for any known extension (`.css`/`.json`/`.svg`/…),
  alongside the existing `.wasm`/`.js` handling; genuinely unknown extensions still serve as
  `application/octet-stream`.
- **Keyed reorders now preserve the survivors' focus, text selection, and caret position**, not
  just their uncommitted input *value*. Applying a trusted structural diff (`MoveSubtree` /
  `PermutationBatch`) — and the keyed branch of the untrusted morph — relocated each surviving
  node with `removeChild` + `insertBefore`. Detaching a node blurs any focused descendant, so the
  typed text travelled with its row (the same DOM node is reused) but focus, selection, and caret
  silently did not — contradicting the "survivors keep their DOM state" contract the Keyed lists
  demo advertises. The runtime now relocates with the Atomic Move API (`Node.moveBefore`,
  Chromium 133+), which moves a node without disconnecting it, and falls back to `insertBefore`
  where it is unavailable. Affects `Rask.Server` (`rask.js`), `Rask.Wasm` (`rask.wasm.js`), and
  the shared morph runtime (`rask-morph.js`); covered by the Keyed lists step of every host's E2E
  journey, which now reverses a list with a focused, mid-string caret and asserts it survives.
- GitHub Pages demo showed `v1.0.0` instead of the released version in the navbar badge. The
  `pages` workflow checked out a shallow clone, so MinVer couldn't read the git tags and the
  assembly kept the .NET default informational version. The workflow now fetches full history
  (`fetch-depth: 0`), matching the CI/release/nightly workflows, so `RaskVersion.Current`
  reflects the real tag.
- `LiveSessionStore` no longer throws `ObjectDisposedException` while retiring pending session
  removals under contention. `ScheduleRemoval` disposed the prior `CancellationTokenSource` from
  inside an `AddOrUpdate` factory — i.e. while it was still reachable through `_pendingRemovals` —
  so a concurrent `CancelPendingRemoval` / `CancelAllPending` (e.g. two sockets detaching at host
  shutdown) could `TryRemove` the same instance in that window and call `Cancel()` on an
  already-disposed source. The install now swaps atomically (`TryUpdate`/`TryAdd`) and retires the
  prior source only after the CAS has made it unreachable, so exactly one thread owns its disposal.
  Fixes flaky `Reconnect_WhileExistingSocketAttached_NewSocketBecomesAuthoritative`.
- `LiveSession` disposal no longer throws `InvalidOperationException: Collection was modified`
  while tearing down the component tree. `Dispose`/`DisposeAsync` walked the tree (enumerating
  each component's persisted children) without holding `_renderLock`, so a render still draining on
  a thread-pool thread at host shutdown could rebuild those same child dictionaries (the swap+clear
  in `BuildRenderTree`) mid-enumeration. Teardown now takes `_renderLock` around the walk — the
  same gate renders already use to keep concurrent tree mutations mutually exclusive — and a
  `_disposed` guard stops a `StateHasChanged` from an unmount/dispose callback re-entering the
  render path. Fixes flaky `HandlerOrderingTests.TwoHandlers_AcrossMultipleRounds_NeverReorder`.
- Showcase samples no longer 404 on `bootstrap.min.css.map`: the vendored `bootstrap.min.css`
  carried a `sourceMappingURL` comment pointing at a map file that isn't shipped, so browsers
  (and the GitHub Pages demo) logged a console 404. Dropped the dangling comment.
- `LiveRenderContext.CurrentSync` no longer returns a disposed context. The thread-static sync
  mirror could linger on a pooled thread after an async render released it at an `await`; a later
  synchronous render reusing that thread observed the stale context (wrong handler attribution).
  Reading through the `IsActive` guard restores the documented "null outside an active render"
  contract. Allocation-neutral (113.9 KB render unchanged). Fixes flaky
  `*_OutsideLiveContext_OmitsHandlerAttribute` tests.

### Added
- New `samples/Rask.Example.EfCore` sample: an EF Core + SQLite CRUD app (Server host) showing data
  persistence in Rask. It uses `IDbContextFactory` (right for long-lived live sessions), organises
  code as vertical slices (List/Create/Edit), models the catalogue with a DDD aggregate + value
  objects whose validation rules are reused by the inline form validators, and stores money as
  integer minor units to sidestep SQLite's lack of a decimal type. Covered by unit + EF/SQLite
  integration tests (`Rask.Example.EfCore.Tests`) and a Playwright CRUD smoke test, and documented in
  `docs/data-access.md`.
- `RaskVersion.Current` exposes the running framework version (from the assembly's MinVer
  `InformationalVersion`). The server (`UseRask`) and WASM host log it on startup, and the
  showcase samples display it as a version badge.
- AI-assistant onboarding: an `AGENTS.md` ships in every `dotnet new rask-*` template (app-author
  conventions), plus a root `AGENTS.md`, `llms.txt`, and `docs/ai-agents.md`.
- Community health files: issue forms, PR template, `CODE_OF_CONDUCT.md`, `SECURITY.md`,
  `CODEOWNERS`, and `docs/repo-administration.md` — contributions open, maintainer merges.

### Removed
- **Breaking:** removed the `Component.User` convenience property. Components that need the current
  principal now inject `IUserProvider` via the constructor and read `.Current` (a never-null
  `ClaimsPrincipal`) — explicit, testable dependencies instead of a base-class service locator. The
  built-in `Authorize` component, `[Authorize]` route gating, and the auth samples/templates are
  unchanged in behaviour.

### Changed
- Build is now **warnings-as-errors** with .NET analyzers and code-style enforced in-build
  (`Directory.Build.props`); see `docs/code-analysis.md`.
- NuGet packages ship a concise, gallery-friendly `NUGET.md` README (absolute URLs) instead of
  the full repo README.

### CI
- `nightly.yml` publishes a prerelease to nuget.org + GitHub Packages on every push to `main`,
  now gated on the **full** test suite — the prerelease only publishes after both the `unit` job
  and the complete sharded `e2e` matrix pass (previously `unit` only).
- `commitlint.yml` enforces Conventional Commits; `dependabot.yml` keeps NuGet/Actions current.

### Documentation
- New guides: `docs/development-workflow.md`, `docs/code-analysis.md`, `docs/ai-agents.md`,
  `docs/repo-administration.md`. CLAUDE.md compacted to a map pointing at the new
  `.claude/skills/` playbooks.

### Security
- URL-bearing attributes (`href`, `src`, `cite`, `formaction`, object `data`, `poster`,
  SVG `href`) are now **scheme-sanitized by default**: `javascript:`/`vbscript:` and
  `data:` outside media tags are neutralized to `about:blank`, closing a DOM-XSS hole that
  HTML-encoding alone left open. Detection defeats whitespace/tab/NUL obfuscation. Opt out
  per call with `RaskUrl.Trusted(...)` for URLs you control; media tags still allow inline
  `data:image/*`, `data:video/*`, `data:audio/*`. See the [getting-started guide](docs/getting-started.md#url-attributes-are-scheme-sanitized).

### Performance
- Scoped-asset registry reads (run per component, per render) are now lock-free
  (`ConcurrentDictionary`), so concurrent sessions no longer serialize on a process-wide lock.
- Removed `AsyncLocal` reads from the per-element attribute path via a thread-local
  render-context mirror, and cache `Component.Key` stringification (no per-render `ToString`
  allocation on keyed lists).
- `<head>` splice avoids a second whole-body scan, pools its builders, and appends keys
  without per-asset string allocation.

### Memory
- The per-render head-asset collector and mounted-type set are hoisted onto the root and
  reused (cleared per render) instead of allocating fresh collections every frame.
- A session minted by the GET shell but never connected over WebSocket is now evicted on a
  short grace (vs. the 30s reconnect grace), and `MaxSessions` is enforced as a hard atomic
  reservation — a concurrent GET burst can no longer exceed the cap.
- The WebSocket handler-dispatch chain is bounded: when handlers back up behind a hung or
  flooding client, the socket is closed instead of retaining queued payloads without limit.

### Changed
- **CI is now parallel.** The single build-then-test job is split into a fast `unit` gate
  (every non-browser test, built without the WASM AppBundle) and an `e2e` job that **shards
  one browser host per runner** so all fixtures boot concurrently instead of in serial
  batches. PR feedback no longer waits on the full E2E suite.
- **E2E suite consolidated to one journey per hosting project** (8 facts, down from ~192). Each
  host now runs a single comprehensive journey: the showcase trio (Server, Wasm.Host,
  StandaloneWasm) walks every page and exercises every browser-observable feature plus unusual
  activity (in-session + deep-link NotFound, back/forward, deep-link refresh, slow-3G throttling,
  offline→WebSocket reconnect, bounded-heap memory loop, CSS-loaded / JS-loaded / global
  error-handling checks); the sub-path host verifies the full `/sub` prefix contract; each auth
  host (cookie/JWT × server/WASM) runs one admin round trip + non-admin check with the token
  at-rest assertions intact. The fine-grained framework/component logic those per-feature facts
  asserted is covered in-process by the unit suites (unit-first).

---

## [0.7.0] - 2026-06-10

### Fixed
- `SessionUploadStore` no longer blocks a thread-pool thread with sync-over-async
  (`.GetAwaiter().GetResult()`) while staging an upload — the copy is now awaited.

### Documentation
- New [Composition](docs/composition.md) guide: children & fragments, callbacks, context,
  `Virtualize`, drag-and-drop.
- New [JS interop](docs/js-interop.md) guide: scoped CSS/JS, `IJSRuntime`, element refs,
  asset delivery.
- XML `<summary>` docs on the public host entry points (`AddRask`, `UseRask`,
  `WasmHostBuilder` / `RunAsync`).
- Added `CONTRIBUTING.md` and this changelog.
- Per-sample and per-template `README.md` files; clearer `--auth` template descriptions.

### Changed
- Showcase sample: the home page is now a grouped feature index, with light UI polish.

---

## Earlier history

Condensed from the commit log; see GitHub PRs for detail.

### Authentication & security
- Production authentication: `Authorize` component, route guards, cookie & JWT for Server
  and WASM, runnable samples, templates and the [authentication guide](docs/authentication.md) (#33).
- Hardened the live session: `returnUrl` handling, WebSocket origin checks, reconnect race
  fix, and a concurrent-session cap (#34).

### Components & DX
- Replaced the `Callback` type with automatic generator-managed parent re-render (#31),
  building on Context, element refs, form groups, and user-gating (#30).
- Added a headless drag-and-drop primitive (#26) and typed SVG components with a showcase (#16).
- Made non-nullable value-type factory parameters required (#25).
- First-class `Component.Key` with auto-forward and the RASK022 missing-key warning (#7).
- Emit `[DebuggerStepThrough]` + a `<see cref>` breadcrumb on generated factories (#35).

### Live runtime & diff codec
- `PermutationBatch` diff op to close the keyed-reorder byte soft spot (#20).
- Pooled the keyed-diff scratch so `FrameDiffer` is allocation-free per session (#17).
- Ship a `<head>` fragment / history-only diff on head- or query-only navigations.

### Infrastructure
- Restructured to `src` / `tests` / `samples` / `benchmarks` with `.slnx` and Central
  Package Management (#14).
- Consolidated shared test helpers into `Rask.TestSupport` (#4).
