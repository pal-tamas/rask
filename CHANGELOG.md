# Changelog

All notable changes to Rask are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions are stamped at pack
time (`$(PackageVersion)`); this log groups changes by the pull request that introduced
them until tagged releases begin.

## [Unreleased]

### Fixed
- **Live diff now updates adjacent text nodes correctly.** Two bare strings rendered side by side
  (e.g. `Button()[ "Toggle ?tab=", value ]`) were emitted as two render frames, but the browser
  coalesces adjacent text into a single DOM node — so the diff's per-frame slot walk drifted past the
  real child nodes and the `UpdateText` patch was silently dropped (the text never changed; surfaced on
  the showcase "Switch user" toggle button after a `Navigator.SetQuery`). Adjacent text frames are now
  coalesced at emission to match the DOM, including across transparent `Fragment`/`Context` boundaries.
- **Empty text children no longer drift sibling positions.** An empty/`null` string child
  (`Div()[Span(), "", Span()]`) produces no HTML and no DOM node, but still emitted a text frame — so
  every following sibling's diff path was off by one. Empty text frames are now skipped.
- **`Raw` adjacent to other siblings falls back to a full morph.** A `Raw`'s verbatim markup can parse
  into zero, one, or many DOM nodes, so a sibling rendered after a non-sole-child `Raw` could be patched
  at the wrong index by an ungated `UpdateText`/`SetAttribute`. When a changed sibling level mixes a
  `Raw` with other nodes, the render now routes to the full-HTML morph (which reparses the markup
  correctly) instead of shipping a mis-targeted positional op. A solitary `Raw` is unaffected.

### Changed
- **`CheckboxGroup`/`RadioGroup` rewritten as Components** (the `MultiSelect<TItem>` template) instead of
  static `Fragment` factories. Each now supports **bound** (`() => model.X` with a per-field `Validate`
  rule + `AfterBind` hooks) and **controlled** (`Value` + `OnChange`/`OnChangeAsync`) modes, and their
  factories are generator-emitted (Bind-first validator fan-out for bound mode). **Note:** as Components
  they are their own re-render boundary, so host-side derived UI updates via the auto-wrapped controlled
  `OnChange` (the showcase group demos moved to controlled mode) rather than the free host re-render the
  old `Fragment` form gave.
- **Generator-driven validator fan-out for bound form controls.** `[GenerateForwarderFactory]` gained a
  `Validator` parameter: naming a `Delegate?` parameter makes the generator emit the none/sync/async
  `Validate<T>`/`ValidateAsync<T>` overloads around a single hand-written core. `Input`/`Select`/`Textarea`
  now declare one `Bound` core each instead of three near-identical overloads + a private `BoundCore` — the
  generator emits the cast-free `Validate` overloads. The generated `Input(…)`/`Select(…)`/`Textarea(…)`
  factory surface is unchanged (no consumer impact). The forwarder fan-out also supports **generic
  components**: it carries the component's type parameters and derives the validator type `T` from the
  `Expression<Func<T>>` Bind parameter, so the sample `MultiSelect<TItem>` declares one `Bound` core (its
  `Validate` over `ICollection<TItem>`) and the hand-written `MultiSelectBoundFactory` is removed.

### Changed
- **`Input`'s `Type` is now a strict `InputType` enum instead of a free string.** `InputType` (in
  `Rask.Core`) is the closed set of HTML input types (`Text`/`Search`/`Tel`/`Url`/`Email`/`Password`/
  `Number`/`Checkbox`/`Radio`/`File`/`Date`/`DatetimeLocal`/`Time`/…), so the type is validated at compile
  time; `Input<T>` still derives the type from `T` when `Type` is unset. New analyzer **RASK025** warns when
  a string-only `InputType` (text family) is set on a non-`string` `Input<T>` (the type would never
  round-trip). **Breaking:** `Type:` takes an `InputType` — `Input<string>(InputType.Text, …)`,
  `Input(() => m.Email, Type: InputType.Email)` — instead of a string literal.
- **Built-in `Input`/`Select`/`Textarea` are now generic `Input<T>`/`Select<T>`/`Textarea<T>` implementing
  `IFormControl<T>`.** Bound usage infers `T` from the expression (`Input(() => model.Age)` → `Input<int>` →
  `<input type="number">` — the HTML input `type` derives from `T`: `bool`→checkbox, numeric→number,
  `DateOnly`→date, etc.); binding is resolved at render time rather than in a `Bound` factory. **Breaking:**
  plain (non-bound) usage now needs the explicit type argument — `Input<string>("text", …)`,
  `Select<string>(…)`, `Textarea<string>(…)` — and bound `Select` takes its options via the `[...]` indexer
  rather than a `Children:` argument. `IFormControl<T>` also gained default-method helpers that remove
  per-control boilerplate (`Validator`, `RegisterValidator`, `InvokeAfterBindAsync`, `InvokeOnChangeAsync`,
  `ControlledChangeHandler` — the string↔`T` bridge for controlled mode), adopted by the built-ins and the
  sample `MultiSelect`/`CheckboxGroup`/`RadioGroup`.

### Added
- **`IFormControl<T>` — a declarative contract for building custom form controls.** A generic component
  implementing `IFormControl<T>` (in `Rask.Core.Forms`) gets both of its factories synthesized by the
  generator: a **controlled** factory (`Value` + `OnChange`/`OnChangeAsync`) and a **bound** factory
  (`() => model.Field`, Bind-first, with the per-field validator fanned into none/sync/async overloads).
  The interface carries the typed `Bind`/`Validate`/`ValidateAsync`/`AfterBind`/`AfterBindAsync` (bound) and
  `Value`/`OnChange`/`OnChangeAsync` (controlled) members over a single value type `T`; the generator
  excludes the bound members from the controlled factory automatically (no `[SkipFactory]` needed). The
  sample `MultiSelect<TItem>`/`CheckboxGroup<TItem>`/`RadioGroup<TValue>` now implement it and drop their
  hand-written `Bound` methods. (Controlled `OnChange` for the collection controls now delivers
  `ICollection<TItem>` rather than `IReadOnlyCollection<TItem>`, matching `Value`.)
- **`Callback` / `Callback<T>` / `CallbackAsync` / `CallbackAsync<T>` delegate types.** Named delegate
  types in `Rask.Core` are now the shape of every built-in component's event callbacks (the `X` / `XAsync`
  pairs: `OnClick`/`OnClickAsync`, `OnInput`/`OnChange`/`…Async`, `OnKeyDown`/`OnKeyUp`, `OnDrag*`,
  `OnScroll`, `OnSubmit`, `OnFiles`, and `Form`'s `OnValidSubmit`/`OnInvalidSubmit`), replacing the inline
  `Action`/`Func<…,Task>` spellings — paired with the `Validate`/`ValidateAsync` convention. `AutoCallback`
  and the live dispatcher (`Component.TryInvokeHandlerAsync`) carry typed overloads/cases for them, so the
  re-render and DOM-dispatch hot paths stay reflection-free. **Standard `Action`/`Func` handlers remain
  supported** for consumer code — these are additive. (`CallbackAsync`, not `AsyncCallback`, to avoid the
  `System.AsyncCallback` clash.) **Minor breaking:** code passing a pre-typed `Action`/`Func` *variable* to
  a built-in handler must switch the variable to the matching `Callback`/`CallbackAsync` type (inline
  lambdas and method groups are unaffected).
- **`Validate<T>` / `ValidateAsync<T>` delegate types.** Named, shared validator delegate types in
  `Rask.Core.Forms` replace the verbose inline `Func<T, IEnumerable<string>>` /
  `Func<T, CancellationToken, ValueTask<IEnumerable<string>>>` as the `Validate` parameter shape on every
  form control (`Input`/`Select`/`Textarea`/`Form`/`MultiSelect`). **Changed (minor breaking):** the
  `Validate` parameter type changes accordingly — lambda call sites are unaffected; a caller passing a
  pre-typed `Func<…>` variable must adjust.
- **Public binding API for custom form controls.** `ExpressionAccessor` (+ its `Accessor` record) and
  `BindingHelpers` (`ResolveBindingContext`, `FormatValue`) in `Rask.Core.Forms` are now public — the
  same machinery the sample `RadioGroup`/`CheckboxGroup`/`MultiSelect` use, so consumers can build their
  own controls that bind to a model property and drive the ambient `EditContext`. `docs/forms.md` §9 is a
  "building form components" guide. See also `EditContext.RegisterFieldValidator` for per-field rules.
- **`BindingHelpers.SetCollectionMembership<T>` and `NotifyAndValidateFieldAsync`.** Two more public
  building blocks for custom bound controls: the first adds/removes an item in a bound `ICollection<T>`
  by a comparer (returns whether it changed); the second commits a field change (marks it changed +
  touched and re-validates, no-op without a context). The sample `MultiSelect`/`CheckboxGroup`/`RadioGroup`
  now share these instead of each hand-rolling the add/remove + notify/validate logic.
- **Multi-select showcase.** A reusable generic `MultiSelect<TItem>` example component
  (`samples/Rask.Example.Shared/Shared/MultiSelect.cs`) — a custom Bootstrap dropdown with removable
  chips, open/close + Esc / click-outside close driven entirely by the server live diff (no client JS).
  Supports two shapes: **bound** (`() => model.Items` with `AfterBind`/`AfterBindAsync` post-bind hooks
  and a per-field `Validate` rule, sync or async) and **controlled** (`Value` + `OnChange`/`OnChangeAsync`,
  no `Bind`). Surfaced on the `/multiselect` page with both bound and controlled demos.
- **Floating-label form controls showcase (samples only).** Reusable `FloatingInput<TProp>`,
  `FloatingSelect<TProp>`, and `FloatingTextarea<TProp>` example components
  (`samples/Rask.Example.Shared/Shared/`) wrap Rask's `Input`/`Select`/`Textarea` + `Label` +
  `ValidationMessage` in Bootstrap 5.3's `.form-floating` markup — one line per field, with the id
  derived from the bound property, the label read from its `[Display(Name)]` attribute, and the
  input type inferred from `TProp`. They own no validation state and need no extra CSS (errors show
  via Bootstrap's `.invalid-feedback .d-block`). Surfaced on a new `/floating-labels` page under the
  Forms nav group.

### Changed
- **Moved `CheckboxGroup<TItem>` and `RadioGroup<TValue>` out of `Rask.Core` into the samples**
  (`samples/Rask.Example.Shared/Shared/`), where they join `MultiSelect<TItem>` as copyable example
  controls built on the public binding API — keeping `Rask.Core` minimal (the framework ships the
  binding API, not a control library). They now render Bootstrap 5.3
  [check markup](https://getbootstrap.com/docs/5.3/forms/checks-radios/) (`form-check` wrapper +
  `form-check-input`/`form-check-label` with `id`/`for`); `ItemClass` now adds extra wrapper classes
  (e.g. `form-check-inline`). **Breaking:** they are no longer part of `Rask.Core` — copy the sample
  files into your project, or build equivalents on `ExpressionAccessor`/`BindingHelpers` (see
  `docs/forms.md` §9).

### Removed
- **Dropped the `TableModel<T>` headless-table primitive and the `Rask.Core.Tables` namespace**
  (`ColumnDef<T>`, `ColumnSort`, `SortDirection`, `HeaderCell`, `TableRow<T>`, `TableModelContext<T>`)
  from the framework, keeping `Rask.Core` minimal. The `/table` and `/master-detail` showcase pages
  now render a plain `Table` directly — they already owned all the sort/page/expand state, so the
  controlled-projection layer added no capability over the standard HTML table components. **Breaking:**
  consumers using `TableModel<T>` should render `Table`/`Thead`/`Tbody`/`Tr`/`Td` themselves (see
  `samples/Rask.Example.Shared/Features/Table/TablePage.cs` for the pattern).

### Security
- **`CSS.escape` the ElementRef reviver selector (both runtimes).** The client reviver that
  resolves an `{"__raskRef__":"id"}` placeholder to a live DOM element now escapes the id via
  `CSS.escape(...)` before building the `[data-rask-ref="…"]` selector, in `rask.js` (Server) and
  `rask.wasm.js` (WASM). Defense-in-depth: ids are framework-minted, but escaping closes any path
  by which a value carrying a quote/bracket could break out of the attribute selector. The reconnect
  overlay is now built with `document.createElement`/`textContent` instead of `innerHTML`, removing
  the only `innerHTML` write on a non-`<template>` path (cleaner under a strict CSP).

### Changed
- **Modernized the client runtime JavaScript.** `rask.js` (Server), the shared diff/morph codec
  (`rask-dom.js`/`rask-morph.js`), the WASM runtime (`rask.wasm.js`) and `main.js` were brought to
  modern JS (`const`/`let`, arrow callbacks, `for…of`, template literals, optional chaining, rest
  params). Behavior is unchanged; the shared spliced helpers keep hoisted `function` declarations
  and emit no `export`/`import` so they remain valid in both the Server's classic-`<script>` IIFE
  and the WASM ES module. Release-build minification stays correct (single-line template literals).
- **Beginner-friendly getting-started rewrite ([docs/getting-started.md](docs/getting-started.md)).**
  Restructured for a developer new to Rask into a "run → understand → extend" path: promoted
  prerequisites, a recommended default template, a *"what you should see"* checkpoint, a new tour of the
  scaffolded files, and a troubleshooting section. Security/edge-case detail (string encoding, URL
  sanitization, the `RenderResult` shapes) moved into clearly-labelled asides instead of interrupting
  the main flow. Surfaced prerequisites up front in `README.md` and `NUGET.md`, and pointed the
  `docs/README.md` index at the guide as the starting point.

### Added
- **Best practices guide ([docs/best-practices.md](docs/best-practices.md)).** A new hub that
  consolidates the patterns and common pitfalls previously scattered across the subsystem docs and
  the RASK diagnostics — component design, rendering/keys, state & callbacks, context/DI, forms, data
  access, security, accessibility, performance, and testing — with each rule linking to its deep
  dive. Indexed from `docs/README.md`, `README.md`, and `llms.txt`.

## [0.10.0] - 2026-06-23

### Added
- **Cooperative handler timeout (`RaskServerOptions.HandlerTimeout`).** `Component.CancellationToken`
  now does double duty: it still cancels on unmount, and *while an event handler is running* it **also**
  cancels when the host cancels that dispatch — the server's new `HandlerTimeout` elapsing, or the socket
  closing. Thread it into the cancellable async work a handler starts (`HttpClient`, `Task.Delay`) and a
  slow handler unwinds cleanly instead of pinning the session's render pipeline; the timeout is logged
  and counted on `rask.handlers.timedout`. Handlers that already pass `CancellationToken` to their async
  calls gain this for free once an operator sets the timeout. It is cooperative — a handler that ignores
  the token can't be force-aborted (a .NET reality; the backpressure and idle-socket caps remain the
  backstop). Opt-in: `HandlerTimeout` defaults to `TimeSpan.Zero` (off), validated at startup like the
  other server limits. See the [configuration](docs/configuration.md) and
  [composition](docs/composition.md) guides.
- **Resource-exhaustion hardening for the server host (all opt-in, default off).** Three new bounds,
  configurable via `RaskServerOptions` / `RaskUploadOptions`: (1) **`IdleSocketTimeout`** closes a
  connected WebSocket that sends no inbound frame for the window — reclaiming silently-idle sockets
  that would otherwise hold a receive loop open indefinitely; the session survives for reconnect under
  the grace period. (2) **`MaxPendingHandlerBytes`** bounds the *aggregate bytes* of queued handler
  payloads (the memory companion to `MaxPendingHandlers`, which bounds only the queue length), so a
  client can't fill the count-bounded queue with large frames and pin gigabytes of cloned payloads;
  tracked on the live session and tripped before cloning. (3) **`RaskUploadOptions.MaxBytesPerSession`**
  caps the cumulative staged-upload bytes a single session may hold at once — an authenticated client
  can no longer accumulate unbounded temp-file storage across requests (a request over the quota is
  rejected with `413`; staged bytes are freed when the session ends). Each new limit is validated at
  startup and defaults to off, so upgrading is a no-op. See the [configuration guide](docs/configuration.md).
- **The WebSocket and session-lifecycle safety limits are now configurable** through a new
  server-host-only options object, `RaskServerOptions`, set via a second `AddRask` callback
  (`configureServer`): the inbound frame-size cap (`MaxInboundFrameBytes`), the handler-backpressure
  cap (`MaxPendingHandlers`), the per-connection inbound rate cap (`MaxInboundFramesPerSecond`), and the
  reconnect / unconnected session grace periods (`SessionGracePeriod` / `UnconnectedSessionGracePeriod`).
  These are server-only (the WASM runtime has no socket server), so they live in `Rask.Server` rather
  than the shared `RaskLiveOptions`. They were previously hardcoded; **all defaults are unchanged**, so
  upgrading is a no-op until you set one. Bind from configuration with
  `AddRask(configureServer: o => builder.Configuration.GetSection("Rask").Bind(o))`; `AddRask`
  validates the values and throws `ArgumentOutOfRangeException` at startup on an out-of-range one (a
  negative grace period, a non-positive frame-size cap), so a misconfiguration fails the boot rather
  than misbehaving at runtime. See the new [configuration guide](docs/configuration.md).
- **Production observability for the server host (OpenTelemetry-aligned).** The Rask server is now
  instrumented with standard .NET primitives — no extra packages, exportable to OpenTelemetry out of
  the box. (1) **Structured logging:** `UseRask<TApp>()` bridges the framework's internal diagnostics
  seam to your `ILogger` pipeline, so every framework fault (a lifecycle hook that threw with no
  ancestor `ErrorBoundary`, a duplicate sibling `Key`, a malformed WS frame, a handler that threw) is
  logged under a stable category (`Rask.Live`, `Rask.Diff`, `Rask.Lifecycle`, …) at the matching level
  with the original exception — replacing the previous scattered `Console.Error` writes. (2)
  **Metrics:** a `Meter` named `Rask.Server` (`RaskTelemetry.MeterName`) exposes session counters
  (`rask.sessions.created` / `.rejected` / `.evicted`), an active-sessions gauge, handler counters
  (`rask.handlers.dispatched` / `.faulted`) and duration histogram, and a `rask.ws.frames.rejected`
  counter tagged by `reason` (`size` / `rate` / `backlog`) — the headline DoS-visibility signal. Read
  with `dotnet-counters --counters Rask.Server` or `AddMeter(RaskTelemetry.MeterName)`. (3) **Tracing:**
  an `ActivitySource` named `Rask.Server` spans each handler dispatch (`rask.handler.dispatch`),
  zero-cost when no listener is attached. (4) **Health checks:** `services.AddHealthChecks()`
  `.AddRaskLiveSessions()` reports live-session capacity — `Healthy` / `Degraded` (≥80% of
  `MaxSessions`) / `Unhealthy` (at the cap). All of it is on by default with no configuration; you opt
  in only to *exporting* it. The render/diff/serialization hot path is untouched; the per-dispatch
  instrumentation (a counter increment, a `Stopwatch` timing pair, a histogram record, and a tracing
  `Activity` that is `null` when no tracer is attached) is allocation-free. See the new
  [observability guide](docs/observability.md).
- **Keyboard events on every element.** `Element` now exposes the `OnKeyDown` / `OnKeyDownAsync` and
  `OnKeyUp` / `OnKeyUpAsync` pairs, the focus-scoped counterpart to `OnClick`, wired by both client
  runtimes (Server WS + WASM) via `data-rask-on-keydown` / `data-rask-on-keyup`. A handler takes
  `Action<KeyboardEventArgs>` (or the async sibling `Func<KeyboardEventArgs, Task>`); the new
  `KeyboardEventArgs` record carries `Key`, `Code`, the `Shift`/`Ctrl`/`Alt`/`Meta` modifiers, and
  `Repeat`. The runtime never `preventDefault`s a key event, so handlers compose with normal typing,
  and the handler storage is hoisted into the lazy `LiveState` so an element with no key handler
  keeps its zero per-instance footprint. The `/todos` sample now closes its dialog on **Escape**
  (focusing the `<dialog>` on open via an `ElementRef`). See the [composition guide](docs/composition.md).
- **A typed async variant for every DOM event handler.** Every event handler now follows the
  `OnClick` / `OnClickAsync` convention — a synchronous `OnXxx` plus an asynchronous `OnXxxAsync`
  (`Func<…, Task>`). This adds `OnScrollAsync` (`Div`) and `OnDragStartAsync` / `OnDragOverAsync` /
  `OnDropAsync` / `OnDragEndAsync` (every `Element`), and gives the keyboard pairs their async
  siblings, replacing the previous untyped single `Delegate?` slots on the drag/keyboard/scroll
  events with discoverable, type-checked pairs. The sync and async siblings coalesce over a single
  backing slot per event (distinguished by delegate type), so the richer surface adds **no
  per-element instance footprint**. The `DragDropContext.Drop(...)` helper is now async-shaped and
  wires to `OnDropAsync`.
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
- **Duplicate-`Key` warning in the live diff.** Two sibling elements sharing the same `Key:`
  (`data-rask-key`) silently disabled keyed reconciliation and fell back to a positional diff, which
  can graft a row's DOM state (focus, input value, scroll position) onto the wrong sibling when the
  list reorders — a hard-to-spot correctness bug. The diff codec now emits a one-time warning naming
  the offending key to standard error when it detects a duplicate (deduplicated, capped, and only ever
  reached on the already-broken path, so a correctly-keyed render pays nothing). See the
  [composition guide](docs/composition.md).

### Changed
- **`Navigator.Navigate(...)` renamed to `Navigator.NavigateTo(...)`.** All three overloads
  (`RouteUrl`, `string` path, and `string` path + query) now read as `nav.NavigateTo(...)` at the
  call site, matching the `NavigateTo` convention. This is a breaking API change with no compatibility
  shim — update call sites accordingly. The `SetQuery`/`RemoveQuery`/`ClearQuery`/`Download` members
  are unchanged.
- **RASK002 no longer fires when a parameterless constructor is available.** The "`required`
  property is incompatible with a DI constructor" warning now only triggers when the component's
  *only* constructor takes dependency-injected parameters (no parameterless ctor). With a
  parameterless ctor present, the generated factory uses the object-initializer path — which *does*
  honour `required` — so the previous warning was a false positive in that case. The diagnostic
  message and `docs/diagnostics.md` now spell out the parameterless-ctor escape hatch, and the
  `Sparkline`/`LiveTicker` samples mark their non-nullable factory parameters `required` (dropping a
  `CS8618` suppression each) to demonstrate it.
- **The `/drag-drop` showcase now splits its two demos into separate `CodeSample` cards.** The
  sortable list and the Kanban board were extracted from a single 178-line `DragDropDemo` into
  `DragDropSortableDemo` and `DragDropKanbanDemo` (each with its own scoped CSS), so the page shows
  one focused source panel per use case — matching the multi-demo layout already used by
  `/virtualize` and `/binding`. Same route, same behaviour.
- **The live-session render pipeline is now shared from `Rask.Core`.** A new `LiveSessionBase` owns
  the render→diff-vs-full→payload build both hosts ran near-identically (`RenderTreeToHtml`,
  `ConsumeDownload`, `WritePayload`) plus the common state (render cache, diff ops, write buffer,
  dedup baseline, the IJSRuntime queue) and the `IRenderHandle`/`ILiveJsHost` plumbing. Server's
  `LiveSession` and WASM's `WasmLiveSession` now extend it, keeping only what genuinely differs —
  their transport (WebSocket push vs in-process `ApplyRender`), locking, reconnect/dispatch
  lifecycle, and send-dedup strategy. Internal only — no API or behaviour change (both hosts'
  full E2E journeys pass unchanged); ~340 lines of duplicate pipeline collapse into one base.
- **The host JS-interop plumbing is now shared from `Rask.Core`.** The pending IJSRuntime-invoke
  queue (`LiveJsInvokeQueue`), the `BeginInvokeJS` deferral + `[JSInvokable]` result envelope
  (`RaskJSRuntimeBase`), and the after-`applyDiff` dispatch loop (`applyFrameInvokes`, in the shared
  `rask-dom.js`) were duplicated between Server (`RaskJSRuntime` / `LiveSession` / `rask.js`) and WASM
  (`WasmJSRuntime` / `WasmLiveSession` / `rask.wasm.js`); both hosts now compose the Core pieces
  through a shared `ILiveJsHost` seam. Internal only — no API change beyond the WASM ordering fix
  noted below.
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
- **Reconnect catch-up frame could be dropped on weak memory models.** The Server `LiveSession`
  socket handoff sets two flags (`_forceResend`, `_renderRequestedWhileDetached`) before publishing
  the new socket, relying on "publish the socket last" so a concurrent background render sees the
  flags before the socket. Only `_forceResend` was `volatile`; `_socket` and
  `_renderRequestedWhileDetached` were plain fields, so the intended store ordering held on x86 (TSO)
  but not on weaker models such as ARM64, where a render could observe the fresh socket yet miss the
  resend flags and skip the reconnect recovery frame. Both are now `volatile`, giving the publish its
  release/acquire semantics across all architectures.
- **A fault while failing pending JS invokes no longer masks the real send error.** When a WebSocket
  send throws with queued `IJSRuntime` invokes in flight, `LiveSession` faults those awaiting tasks
  locally before rethrowing. That cleanup is now wrapped so a throw inside it (e.g. an unavailable
  runtime) is logged rather than propagated in place of the meaningful send exception — matching the
  method's documented best-effort contract and ensuring the original error reaches the caller.
- **RASK002 now catches the two cases where a parameterless ctor does *not* rescue a `required`
  property.** The previous narrowing (only warn when no parameterless ctor exists) had two gaps.
  First, a `required` property that *also* carries a member initializer is excluded from the factory
  parameters, so the object-initializer path never assigns it — the consumer build failed with a
  cryptic `CS9035` inside generated code and no diagnostic; RASK002 now fires for this combination.
  Second, the diagnostic message, `docs/diagnostics.md`, and the `/components` showcase all
  recommended "add a parameterless constructor" as a fix — but doing so while keeping the DI
  constructor makes the factory build the component with `new C()` and silently skip the DI ctor,
  leaving injected services `null` at render time (a runtime `NullReferenceException` in place of the
  former compile-time nudge). The guidance now warns against that trap and points to dropping the DI
  constructor or moving the value to a constructor parameter instead.
- **A single malformed WebSocket frame no longer tears down the live session.** The Server receive
  loop parsed each inbound frame with an unguarded `JsonDocument.Parse`, and a valid-JSON-but-non-object
  root (a bare array/number/string) reached `TryGetProperty`, which throws on non-objects — either way
  the exception escaped the loop's `OperationCanceledException` / `WebSocketException` catches, detached
  the socket and scheduled the session for removal. One buggy or adversarial frame could drop a whole
  session. Such frames are now dropped (logged once) and the loop keeps serving; the existing 8 MB
  inbound-frame cap still bounds memory.
- **Component teardown is now strictly one-shot.** A tree mutation inside an `OnUnmount` hook (clearing
  persisted children, re-parenting) could route a node through a second dispose pass, firing `OnUnmount`
  and the user's `Dispose()` twice. `DisposeComponentTree`/`DisposeComponentTreeAsync` now guard on a
  per-component flag so the unmount → cancel → dispose sequence runs exactly once. (The lifetime
  `CancellationTokenSource` was already idempotent.)
- **The deferred-session-removal task can no longer surface an unobserved `ObjectDisposedException`.**
  A reconnect or a reschedule that disposed the pending-removal `CancellationTokenSource` while the
  delayed task was about to read `cts.Token` produced an exception the `OperationCanceledException`
  catch missed. The token is now captured before the task starts and `ObjectDisposedException` is
  handled, so an obsolete removal exits cleanly without orphaning the session.
- **WASM now runs `IJSRuntime` calls issued *during* a render after the DOM is patched, matching
  Server.** Interop from a lifecycle hook — e.g. `OnRenderedAsync` focusing a dialog as it opens —
  used to dispatch immediately on WASM, *before* that render's DOM patch, so a `focus()` could hit a
  still-`display:none` element (a `<dialog>` whose `open` attribute the diff hadn't added yet) and
  silently no-op. Such mid-render calls now ride the render frame's `jsInvokes` and the client
  dispatches them after `applyDiff`, against the committed DOM — the post-commit ordering the Server
  already had. Interop from event handlers is unchanged (WASM still dispatches it immediately, the
  path awaited handler interop relies on). The `/todos` dialog now auto-focuses on open on every
  host, so Escape-to-close works without a prior click.
- **The `/todos` showcase dialog now opens centered over a dim backdrop.** A declaratively-open
  `<dialog open>` is non-modal, so the browser left it `position:absolute` at its in-flow spot (low
  on the page, partly off-screen) with no `::backdrop`. The `TodoFormDialog` scoped CSS now pins the
  open dialog to the viewport centre (`position:fixed; inset:0; margin:auto`) above a clickable
  `.todo-backdrop` overlay — clicking the backdrop cancels, mirroring the nav-drawer pattern. No JS;
  the URL-driven open/close (New todo, Cancel, browser Back, deep links) is unchanged.
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

### Security
- **Inbound WebSocket frame-rate cap.** The Server receive loop now closes a connection that sends
  more than 1000 frames/second (sliding one-second window). This complements the existing per-frame
  size cap (8 MB) and handler-backlog breaker (512 queued dispatches), which don't bound a flood of
  small non-handler frames (`jsResult` / `navigate` / malformed) — each of which still costs a JSON
  parse, so a high-rate stream was a CPU-DoS gap. On a trip the socket closes with a policy-violation
  status and the client reconnects against the intact session. The cap is a fixed internal default
  (far above any legitimate interaction burst); operators should still front the app with a
  reverse-proxy / WAF rate limit for connection-count and cross-connection floods. See the
  [hardening reference](docs/authentication.md#hardening-reference).
- **Suppressed the unactionable `SQLitePCLRaw.lib.e_sqlite3` audit advisory (`GHSA-2m69-gcr7-jv3q`).**
  The native SQLite package arrives transitively through the latest `Microsoft.EntityFrameworkCore.Sqlite`
  (used only by the `Rask.Example.EfCore` sample); the whole SQLitePCLRaw family tops out at `2.1.11`
  and the advisory reports no patched version, so the solution-wide NuGet audit (run as
  warnings-as-errors) failed `restore` for every job. A scoped `NuGetAuditSuppress` for this single
  advisory unblocks the build while leaving audit active for everything else; it is annotated to be
  removed once a patched SQLitePCLRaw family ships.
- **New `RASK024` analyzer — `UseAuthentication()` must precede `UseRask()`.** A compile-time warning
  when `app.UseRask<App>()` is wired before `app.UseAuthentication()`. Rask seeds the live session from
  `HttpContext.User` during the initial GET render and the WebSocket upgrade; if authentication runs
  after `UseRask`, the principal is empty at that point and every `[Authorize]` page challenges — a
  silent, easy-to-miss misconfiguration the docs previously only warned about in prose. Fires only when
  both calls are present and `UseAuthentication` is positioned after `UseRask`; an app with no
  authentication middleware is left alone. Documented in [diagnostics](docs/diagnostics.md#rask024).
- **CI now scans for vulnerable and deprecated dependencies.** The `unit` job runs
  `dotnet list package --vulnerable --include-transitive` and `--deprecated` and fails on findings —
  defence-in-depth beyond restore-time `NuGetAudit` (which doesn't cover transitive or deprecated
  packages). The accepted-and-suppressed SQLitePCLRaw advisory is excluded, and "Legacy" (superseded
  but functional) deprecations are reported as informational rather than failing the build.
- **The scaffolded WASM JWT token store now warns when it holds a plaintext token.** The
  `dotnet new rask-wasm --auth` `TokenStore` keeps the bearer JWT in `localStorage` (a development
  floor — readable by any script via XSS); it now logs a one-time `console.warn` steering to an
  HttpOnly cookie or `ProtectedTokenStore` so a scaffold shipped to production unchanged surfaces the
  risk. New [reverse-proxy `ForwardedHeaders` guidance](docs/authentication.md) documents that the
  host-only anti-CSWSH / redeem-CSRF checks need forwarded headers behind a TLS-terminating proxy, or
  legitimate same-origin WebSocket handshakes are rejected with `403`.
- **File downloads now send `X-Content-Type-Options: nosniff`.** The one-shot download endpoint serves
  its content-type from whoever staged the entry — often echoed verbatim from a client upload — so a
  mislabelled file could be MIME-sniffed by the browser. The response now sets `nosniff` alongside the
  existing `Content-Disposition: attachment` and same-origin / same-session-owner guards, matching the
  asset endpoints. The `Raw` component also gained an XML-doc XSS warning making explicit that it is the
  framework's only un-encoded output path and must never carry untrusted input.
- **Upload filenames are sanitized before they are stored and echoed.** A client-supplied upload filename
  is attacker-controlled and is returned in the upload response (`name`) for hosts to display. Staged
  files were always written to a server-generated token path (never the name), so there was no
  server-side traversal — but the echoed name could carry directory components (`../../etc/passwd`) or
  control characters. The upload endpoint now reduces it to a safe leaf (drops `/` and `\` directory
  components whatever the host OS, strips control/NUL characters, caps the length at 255, and falls back
  to `file` for empty / path-dot inputs) as defence in depth. The returned `name` is still
  attacker-controlled and must be HTML-encoded by hosts before display — never bound into `Raw`.
- **WASM sign-out now guards against open redirects.** `WasmAuthSignIn.SignOutAsync` SPA-navigated to
  its `returnUrl` argument verbatim — commonly a `?returnUrl=` query value an attacker can shape — so a
  crafted value (`//evil.com`, `https://evil.com`, a `\`-prefixed variant) could redirect the user
  off-origin after logout. It now passes `returnUrl` through the shared `LocalUrl.Sanitize` rule (the
  same open-redirect guard the server sign-in path already applies at dispatch), collapsing anything
  non-local to `/` before navigating.
- **Content Security Policy guidance.** The [authentication guide](docs/authentication.md#content-security-policy)
  now documents how to run Rask under a strict CSP. Rask's runtime is built for it — the runtime script
  and scoped assets are external (`<script src>` / `<link>`, no inline JS), and events bind via
  `data-rask-on-*` + `addEventListener` — so `script-src 'self'` suffices (add `'wasm-unsafe-eval'` on
  the WASM host); only `style-src` needs `'unsafe-inline'` for the inline `style=""` the `Style:`
  parameter emits, and the same-origin live WebSocket is covered by `connect-src 'self'`. Includes a
  copy-paste middleware baseline and a new security-checklist item.

### Performance
- **Lower render allocations for elements carrying `Data` / `Aria` / `TabIndex`.**
  `Element.WriteAttributes` iterated the `Data` and `Aria` bags through the
  `IReadOnlyDictionary<,>` interface, boxing an enumerator on every render of an attribute-bearing
  element, and formatted `TabIndex` with `int.ToString()`, allocating a string per element. The bags
  now take a `Dictionary<,>` struct-enumerator fast path (the common `new() { … }` literal), and
  `TabIndex` formats straight into the pooled `StringBuilder` via a new integer `AppendAttr` overload
  (zero allocation on the no-frame-sink path). Measured on the render benchmarks (M-class, ShortRun):
  a 1,000-row data-rich tree dropped **661 KB → 552 KB** allocated (−16.5%), and a 1,000-row
  a11y-rich tree (`Aria` + `Role` + `TabIndex`) **808 KB → 677 KB** (−16%); small/medium trees −25–30%.
  No change to rendered output or the documented attribute order. New `AccessibilityAttributesBenchmarks`
  locks in the a11y path.
- **Halve the diff-codec allocation when a keyed list grows.** `FrameDiffer` sliced each inserted
  subtree's HTML out of the rendered document with a `Substring` while diffing — one short-lived string
  per `InsertSubtree` op. Each op now carries the fragment's `[HtmlStart..HtmlEnd)` char range instead,
  and `LivePayload.BuildPayloadUtf8Diff` slices it straight into the UTF-8 wire buffer at write time
  (`Utf8JsonWriter.WriteStringValue(ReadOnlySpan<char>)`), so no intermediate string is materialised.
  Directly-constructed ops still ship a verbatim `Value`, so the wire format is byte-identical. Measured
  on the new `FrameDifferBenchmarks.InsertRows` (M-class, ShortRun): a 100-row list gaining 50 rows
  dropped **11.32 KB → 5.11 KB** allocated (−55%), and a 1,000-row list gaining 500 rows
  **113.27 KB → 50.81 KB** (−55%). Reorder/no-change/text paths are unchanged.
- **Cheaper attribute-name symbol table in the diff payload.** `LivePayload.BuildPayloadUtf8Diff` built
  an attribute-name count map (and, in a burst, an index map + names list) on every diff to intern names
  appearing 3+ times. A diff with fewer than 3 ops can never reach that break-even, so the whole pass is
  now skipped for the common small update; larger diffs reuse per-thread scratch collections instead of
  reallocating the count map each frame. Measured on the new `AttributeDiffPayloadBenchmarks` (M-class,
  ShortRun): a 2-attribute update dropped **424 B → 208 B** allocated (−51%) and a 100-op
  single-name burst **728 B → 208 B** (−71%) — the 208 B floor is the `Utf8JsonWriter`'s own state.
  Wire output and the interning threshold are unchanged.

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
