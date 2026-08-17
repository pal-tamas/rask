# Best practices

You've read [getting started](getting-started.md) and shipped a component or two. This page
collects the patterns that keep a Rask app correct, secure, and fast as it grows — the rules the
framework rewards, the foot-guns it can't stop for you, and where each one is enforced.

Every item is short on purpose: a rule, *why* it matters, and a link to the deep dive. Many of these
are also compile-time diagnostics ([RASK001–034](diagnostics.md)) — when the analyzer can catch a
mistake, the rule notes the ID.

- [Component design](#component-design)
- [Rendering, keys & encoding](#rendering-keys--encoding)
- [State, callbacks & events](#state-callbacks--events)
- [Context & dependency injection](#context--dependency-injection)
- [Forms & validation](#forms--validation)
- [Routing & lifecycle](#routing--lifecycle)
- [Data access & side effects](#data-access--side-effects)
- [JavaScript interop & refs](#javascript-interop--refs)
- [Security](#security)
- [Accessibility](#accessibility)
- [Performance & memory](#performance--memory)
- [Testing](#testing)
- [Common pitfalls](#common-pitfalls)

---

## Component design

- **Make every component `sealed`.** Components aren't an inheritance hierarchy; sealing states
  intent and keeps the generator's analysis simple. Every sample does this.
- **Construct components through the generated factory, never `new`.** `Div()`, `Counter()`,
  `RatingStars(...)` — the factory wires keys, children, callbacks, and DI that `new` skips.
  Outside `Rask.Core`, `new`-ing a component is **RASK014**. (Test files that define their own
  `Component` subclasses opt out with `#pragma warning disable RASK014`.)
- **Inject framework services through the constructor, not as properties.** A non-nullable settable
  property becomes a *required factory parameter* — so `public IJSRuntime Js { get; set; }` would
  force callers to pass it. Take services as ctor parameters instead:
  ```csharp
  public sealed partial class Weather(IWeatherForecastService service) : Component { /* ... */ }
  ```
  Combining a `required` property with a DI constructor is contradictory — that's **RASK002**.
- **Know what becomes a factory parameter.** The generator derives parameters from your public
  settable properties: non-nullable + no initializer → **required** (a hidden **RASK001** suggests
  marking it `required` so it reads as intentional); nullable → optional defaulting to `null`; an
  initializer (`= ...`), `[SkipFactory]`, or `Children` → excluded. Reach for an initializer or
  `[SkipFactory]` to keep internal state out of the factory signature. See
  [getting started §6](getting-started.md#6-why-homepage-already-exists-factory-generation).

## Rendering, keys & encoding

- **Give every list item a stable, unique `Key:`.** Keys are the reconciliation identity the live
  diff uses to *move* a row instead of rebuilding it — preserving focus, input value, and scroll on
  reorder. A keyless list item is **RASK022**; duplicate sibling keys make the diff fall back to a
  positional walk (and report a one-time `data-rask-key` error via the diagnostics seam — treat it as
  a bug). Use
  entity IDs, not loop indices. See [composition](composition.md#children--fragments).
- **Trust `Text` for anything user-supplied; reserve `Raw` for markup you control.** A plain string
  becomes a `Text` node and is **HTML-encoded**; `Raw(...)` emits verbatim. User input through `Raw`
  is an XSS hole. See [getting started → your first component](getting-started.md#4-your-first-component).
- **Leave the page shell to the framework.** The `TApp` root renders into `<body>`; Rask emits the
  doctype, `<html>`, `<head>` and `<body>` around it (the runtime `<script>` is appended to `<body>`,
  `<head>` is filled from each component's `Head` override). Rendering the shell yourself is
  **RASK021** — a second document nested inside the body, which the HTML parser silently unwraps. Set
  `<html lang>` with the `HtmlLang` override and `<body class>` with `BodyClass`; for anything else,
  override `Shell(head, body)` and place both parameters.
- **Contribute to `<head>` via the `Head` override, not `Head()` children.** `Head()` is a managed
  slot; passing it children is **RASK019**. Override `protected override Component? Head` instead;
  `<title>`/`<base>` are singletons where the last contributor wins. See
  [getting started §7](getting-started.md#7-the-document-and-the-head-override).
- **Don't fight the attribute order.** Universal attributes always render
  `id, class, style, data-*, role, tabindex, aria-*`, then tag-specific. Tests assert it and it's
  stable across releases — match it when asserting on HTML.

## State, callbacks & events

- **Raise child→parent events with a plain delegate prop.** There is no `EventCallback` type — use
  `Action`, `Action<T>`, `Func<Task>`, or `Func<T, Task>`. The factory wraps it so invoking it
  re-renders the parent that owns the lambda, with no `StateHasChanged` by hand. Write the lambda
  *inside* the component so it captures `this`:
  ```csharp
  RatingStars.Value(_rating).OnRate(n => _rating = n)   // lambda captures this → parent re-renders
  ```
  A lambda over a plain local or a static method isn't wrapped and won't trigger a re-render. See
  [composition → callbacks](composition-callbacks-context.md#callbacks-child--parent).
- **Don't expect a handler-only re-render to refire `OnPropsChanged`.** Auto-wrapped delegates are
  excluded from the `propsChanged` diff — changing only the lambda's identity doesn't refire it.
  `OnPropsChanged*` fires when a *bound* value (a prop, a route/query param) actually changes. See
  [lifecycle → when OnPropsChanged refires](lifecycle.md#when-onpropschanged-refires).
- **Let the runtime re-render for you; call `StateHasChanged()` only for out-of-band state.** An
  awaited event handler and each `await` in an async lifecycle hook auto-re-render. You only call
  `StateHasChanged()` by hand for state that changes *outside* the handler-dispatch window — a timer
  tick, a fire-and-forget continuation, or an external event/observable you subscribed to in
  `OnMount`.
- **Thread `CancellationToken` into the async work a handler or hook starts.** The token cancels on
  unmount and — while a handler runs — when the host cancels that dispatch (the server's
  `HandlerTimeout` or a closed socket). Without it, slow work pins the session's render pipeline:
  ```csharp
  Button.OnClickAsync(async () => _rows = await _api.LoadAsync(CancellationToken))["Load"]
  ```
  See [composition → cancelling async work](composition-callbacks-context.md#callbacks-child--parent) and
  [lifecycle → cancellation](lifecycle.md#cancellation-tied-to-component-lifetime).

## Context & dependency injection

- **Use `Context` to skip prop drilling, not as a general data bus.** `Context.Provide<T>(Value:)`
  near the top, then `Context.Get<T>()` / `Required<T>()` / `Has<T>()` *inside `Render()`* below.
  Reading a context value latches the consumer out of the render cache, so it stays reactive even
  through a render-cached intermediate — that's the point. Provide a concrete type and consume by an
  interface if you like. See [composition → context](composition-callbacks-context.md#context-provide--consume).
- **Always pair a manual subscription with its teardown.** If a component *above* the `Router()` (a
  sidebar, breadcrumb) needs to react to navigation or a store, subscribe in `OnMount` and
  unsubscribe in `OnUnmount` — otherwise the publisher keeps a strong reference to the unmounted
  component:
  ```csharp
  protected override void OnMount()   => route.Changed += StateHasChanged;
  protected override void OnUnmount() => route.Changed -= StateHasChanged;
  ```
- **A prop that is a mutable collection does not re-render the child when you append to it.** Props
  are compared with `EqualityComparer<T>.Default`, which for a `List<T>` is reference equality — so a
  parent that appends to a list it owns and calls `StateHasChanged()` re-renders *itself*, while the
  child holding that same list is served from the render cache and never sees the new entries:
  ```csharp
  // the parent appends to _log and re-renders; LogView shows the OLD contents forever
  LogView.Entries(_log)
  ```
  Three ways out, in order of preference: hand the child a fresh snapshot (`_log.ToArray()`) so the
  reference genuinely changes; give the child something to subscribe to; or, when the child plainly
  reads state it does not own and there is no event to subscribe to, opt it out with
  `protected override bool BypassRenderCache => true;`. The same rule is what makes `Router` and
  `Outlet` opt out — they publish per-frame route state the rest of the walk depends on.

## Forms & validation

- **Bind two-way with a `Bind` expression.** `Input.Bind(() => _model.Name)` replaces `Value` +
  `OnInput`/`OnChange` + parsing, and infers the input type from the property's CLR type. `string`
  fields update per keystroke; other types update on blur. It also *replaces* them: a bound control
  installs its own write-back, so `Value`/`Checked`/`OnInput`/`OnChange` are not offered on a bound
  chain (and `AfterBind` is not offered on a controlled one) — reach for `AfterBind` when you want a
  side effect on each bound write. See [forms §1](forms.md#1-two-way-binding).
- **Wrap inputs in `Form<TModel>` and add exactly one validator.** The form owns the `EditContext`
  (touched/modified state + the validator pipeline). One `DataAnnotationsValidator()` or
  `FluentValidationValidator(...)` at the top covers the whole reachable object graph — including
  nested sub-objects and collections. Pick the lightest layer that fits: inline `Validate:` lambdas
  (no package), DataAnnotations, FluentValidation, or async. See [forms §2–§6](forms.md).
- **Bind collections with `foreach` + per-item capture** — the canonical pattern. Each iteration
  closes over a distinct instance, so each row owns its validation state and `foreach` has no closure
  trap:
  ```csharp
  foreach (var item in _model.Items)
      rows.Add(Tr[Td[Input.Bind(() => item.Description)]]);
  ```
  Only reach for the indexer style (`() => _model.Items[i].Name`) when you need the row number or
  replace records rather than mutate them — and then copy the loop index into a per-iteration local.
  See [forms §7](forms-advanced.md#nested--complex-models).
- **Reuse one validation rule across the form and the domain.** A value object that exposes its rule
  as a `static IEnumerable<string> Validate(T value)` (the shape of an inline validator) can be
  passed as a method group to `Input(() => _form.Price).Validate(Money.Validate)` *and* enforced inside the aggregate —
  one source of truth. See [data access](data-access.md#how-the-sample-is-organised).

## Routing & lifecycle

- **`[Route]` registers a page; bind URL pieces with `[RouteParam]` / `[QueryParam]`.** Path
  segments use `[RouteParam]`, query keys use `[QueryParam]` — swapping them is **RASK006**. Bound
  types must be `string` or `IParsable<T>` (**RASK011** otherwise), and must match a route constraint
  (`{id:int}`) when present. Link with the generated, refactor-proof `Routes.Page(...)` builder, not
  hand-written paths. See [routing](routing.md).
- **Navigate from event handlers only.** Every `Navigator` method throws if called during `Render()`
  or the initial GET — it would mid-render the page out from under itself. Load-time redirects belong
  in a route guard, not `Render()`. See [routing → Navigator](routing.md#programmatic-navigation--navigator).
- **Put the right work in the right hook.** `OnMountAsync` for a one-time load; `OnPropsChangedAsync`
  to reload when a route/query param changes; `OnRenderedAsync` for post-paint side effects (it's
  loop-safe — a re-render elsewhere won't refire it). Each `await` auto-re-renders, so mutate state
  after the await and it paints. See [lifecycle](lifecycle.md).
- **A faulted async hook is silent** — the framework logs to `Console.Error` and does *not* re-render
  or surface an error. The classic symptom is a component stuck on its loading placeholder. Wrap
  risky hook work in `try/catch` to render your own error state, or use an `ErrorBoundary`. See
  [lifecycle → gotchas](lifecycle.md#gotcha-a-faulted-async-hook-takes-the-page-not-the-component).
- **Never `StateHasChanged()` in unmount** — the component is already leaving the tree, so it's a
  no-op by design. Use `OnUnmount` to tear down subscriptions, nothing more. Compose nested layouts
  with `[ParentRoute]` + `Outlet()`.

## Data access & side effects

- **Register `IDbContextFactory<T>`, not a scoped `DbContext`.** A Server session is long-lived; a
  `DbContext` is not thread-safe and is meant to be short-lived. Open a fresh context per unit of
  work and dispose it:
  ```csharp
  await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken);
  var products = await db.Products.AsNoTracking().ToListAsync(CancellationToken);
  ```
  Thread `Component.CancellationToken` into every async EF call so navigating away cancels in flight.
- **Load in a lifecycle hook, store in a field, render the field** — never query in `Render()`
  (which runs on every keystroke). For an event-handler mutation, do the work and reload; the awaited
  handler re-renders on completion automatically. See [data access](data-access.md).
- **Keep EF Core on the Server.** The SQLite provider isn't a fit for the trimmed WASM runtime — a
  WASM app should reach data through an API. Watch the [SQLite `decimal`
  gotcha](data-access.md#does-sqlite-support-decimal-the-money-gotcha): model money as integer minor
  units, not `decimal`.

## JavaScript interop & refs

- **Mint element refs with `ElementRef.New()` stored in a field.** A field keeps the ref id stable
  across renders (a local resets each render). Pass it via `Ref:`, then hand it to JS or a built-in
  helper (`_input.FocusAsync(_js)`). See [JS interop → element refs](js-interop-runtime.md#element-refs).
- **Inject `IJSRuntime` through the constructor and call from a hook or handler** — interop is only
  live once the session is up (after `OnMount`, or inside handlers). One scoped `{Component}.css` /
  `{Component}.js` sits next to `{Component}.cs` and is auto-included and isolated; orphan or
  ambiguous assets are **RASK015–018**, and two scoped-JS components sharing a simple type name
  collide at `window.Rask[Name]` (**RASK020**).
- **Put global styles in `wwwroot`, not a scoped CSS file.** Scoped CSS has no opt-out selector, so a
  brand palette, `:root` variables, shell tags, or Bootstrap belong in a plain stylesheet linked from
  your App's `Head` (use `LiveOptions.PathBase` for the URL). See [JS interop → scoped
  CSS](js-interop.md#scoped-css).

## Security

- **Order middleware `UseAuthentication()` → `UseAuthorization()` → `UseRask<App>()`.** Rask seeds
  the session from `HttpContext.User` on the initial GET and the WS upgrade; if auth runs *after*
  Rask the principal is empty and every `[Authorize]` page challenges. This is **RASK024**. Behind a
  reverse proxy, wire `UseForwardedHeaders()` *first* so the origin checks see the public host.
- **Prefer the cookie scheme; keep tokens out of JavaScript.** An HttpOnly cookie is immune to XSS
  token theft. If a token must live in the browser (standalone WASM), store it **encrypted**
  (`ProtectedTokenStore`) or at least short-lived in `sessionStorage` — never plaintext
  `localStorage` in production (the scaffold's plaintext store is the floor, not the recommendation).
- **Gate interactive WASM/JWT content with the `Authorize` component, not route `[Authorize]`.** A
  bearer challenge returns 401, not a login redirect, so route gating is the wrong tool for an
  interactive page; `Authorize(Authorized:, NotAuthorized:, Authorizing:)` gates on the local
  principal.
- **Lean on the built-in URL sanitization.** URL-bearing attributes neutralize dangerous schemes
  (`javascript:`, `vbscript:`) to `about:blank` by default; use `RaskUrl.Trusted(...)` only for URLs
  you control. Treat the **session id as a bearer secret** (HTTPS only, never logged), and set a
  strict **Content-Security-Policy** as middleware before `UseRask` — Rask needs no
  `script-src 'unsafe-inline'` (only `style-src 'unsafe-inline'` for `Style:` attributes, plus
  `'wasm-unsafe-eval'` on WASM). Full flows and the [security
  checklist](authentication-hardening.md#security-checklist) live in [authentication](authentication.md).

## Accessibility

- **Always give `Img` an `Alt`.** A meaningful string for informative images, `Alt: ""` for
  decorative ones (so screen readers skip them). A missing alt is **RASK023**. See
  [accessibility](accessibility.md#images-and-alt-text-rask023).
- **Reach the full ARIA vocabulary through the `Aria` dictionary**, with typed `Role` and `TabIndex`
  for the two attributes that aren't `aria-*`. Build higher-level affordances from these primitives
  plus semantic HTML (`Nav`, `Main`, `Label(For:)`, `Th(Scope:)`):
  ```csharp
  Div.Role("status").Aria(new() { ["live"] = "polite" })[_statusMessage]
  ```

## Performance & memory

- **Key your lists — it's a performance rule too.** Keyed insert/remove/move ship as small *trusted*
  diff ops that preserve DOM identity; keyless structural changes fall back to a full-HTML morph. See
  [architecture → keyed reconciliation](architecture/live-rendering-codec.md#keyed-reconciliation-trusted-structural-ops).
- **Treat `Key` as identity, not a reactive signal.** Changing a key mounts a fresh instance; it
  doesn't refire `OnPropsChanged`.
- **Use a `[...]` collection expression to avoid a wrapper node** for sibling lists, and `null` for a
  "render nothing" branch (`show ? Panel() : null`).
- **Benchmark every render-hotpath or live-runtime change.** Diff codec, frame writer, serializer,
  and dispatch are under measurement — run `benchmarks/Rask.Benchmarks` before/after and quote the
  `Allocated` delta. See [development workflow](development-workflow.md).

## Testing

- **Unit-test first; reach for E2E only when a path is genuinely unreachable** by a unit test (the
  real JS transports, `rask.js` DOM application, real auth handshakes, browser layout). E2E is heavy.
- **Render with the right entry point.** `ToHtml()` for a standalone component; wrap in
  `StubComponent` and call `RenderAsLiveRoot()` for anything needing a live context (handlers, forms,
  DI). Drive handlers via the `data-rask-on-*` id + `TryInvokeHandlerAsync`, and assert exact
  attribute order. See [testing](testing.md).
- **Every `samples/` change gets an E2E test.** Add a Playwright journey to
  `tests/Rask.Examples.E2E.Tests`.

## Common pitfalls

| Pitfall | Do instead |
|---|---|
| List items with no `Key:` (**RASK022**) — focus/input lost on reorder | Pass a stable, unique `Key:` (entity id) |
| `new Counter()` outside Core (**RASK014**) | Call the generated factory `Counter()` |
| Service as a settable property → required factory param (**RASK002**) | Inject via the constructor |
| `Head()[Title()[...]]` (**RASK019**) | Override `protected override Component? Head` |
| Root renders `Doctype`/`Html`/`Head`/`Body` (**RASK021**) | Return the body's content; `Head`/`HtmlLang`/`BodyClass`/`Shell` |
| User input through `Raw(...)` (XSS) | Use a plain string / `Text` (encodes by default) |
| `StateHasChanged()` inside an awaited handler/hook | Redundant — the `await` re-renders for you |
| `StateHasChanged()` in `OnUnmount` | No-op by design — only tear down subscriptions |
| Subscribing to an event without unsubscribing | Pair `+=` in `OnMount` with `-=` in `OnUnmount` |
| Scoped `DbContext` in a Server app | `IDbContextFactory<T>` + a fresh context per op |
| Async EF/HTTP calls without the token | Thread `Component.CancellationToken` through |
| `localStorage` plaintext JWT in production | HttpOnly cookie, or encrypted/short-lived storage |
| `UseAuthentication()` after `UseRask()` (**RASK024**) | Auth → Authorization → Rask, in that order |
| `Img` without `Alt` (**RASK023**) | Real alt text, or `Alt: ""` for decorative |
| `for`-loop index captured in a binding lambda | Copy to a per-iteration local, or use `foreach` |

---

See also the [diagnostics reference](diagnostics.md) for every RASK0xx ID and its fix, and
[CLAUDE.md](../CLAUDE.md) for the contributor-facing map of the framework internals.
