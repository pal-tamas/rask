# Changelog

All notable changes to Rask are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions are stamped at pack
time (`$(PackageVersion)`); this log groups changes by the pull request that introduced
them until tagged releases begin.

## [Unreleased]

### Fixed
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
