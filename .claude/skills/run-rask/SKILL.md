---
name: run-rask
description: Build, launch, and drive the Rask site (src/Rask.Site) — the one browser-WASM app behind rask.sh, with the landing page at / and the showcase plus guides at /docs. Use to run/start/launch the app, take a screenshot, or confirm a UI change works in the real running app (not just tests). Drives it headlessly with a committed C# Playwright driver — pure .NET, no Node. Also drives the built-in operator console (Rask.Dashboard at /_rask) at desktop and phone widths, out of a throwaway scaffolded app that ops-app.sh builds against the working tree.
---

# Run the Rask site

`src/Rask.Site` is the framework's showcase and its published front door: the landing page at `/`,
the guides and every live demo at `/docs`. It is **browser-WASM** — the browser downloads
`dotnet.wasm` plus the assemblies, boots the Mono runtime, and renders and handles events locally via
JSImport/JSExport. There is no server and no WebSocket. It's the app to launch when you want to *see*
a change.

**`curl` is useless here.** It sees the boot shell and nothing else, and the framework assets are
fingerprinted and resolved through the page's import map, so `/_framework/dotnet.js` even **404s on a
direct GET**. The app only exists once a real browser boots the runtime. The committed driver
`.claude/skills/run-rask/driver.cs` does that with the repo's existing **Microsoft.Playwright** (.NET)
dependency — the same version the E2E suite uses, browsers already in the `ms-playwright` cache. It's
a .NET 10 *file-based app*: **no Node, no npm, no csproj** — just `dotnet run driver.cs`.

All paths below are relative to the repo root (the unit). Run everything from there unless told
otherwise.

## Prerequisites

- **.NET 10 SDK** — that's the whole toolchain; the driver pulls `Microsoft.Playwright` via NuGet
  (version comes from `Directory.Packages.props`, so the `#:package` directive is intentionally
  unversioned — Central Package Management supplies it).
- **Playwright browsers** — already present in `~/Library/Caches/ms-playwright` (installed for the
  E2E suite). If missing, the driver errors with a "browser not found" message; build the E2E project
  once, then `scripts/playwright.sh install chromium`. That wrapper drives the node CLI bundled with
  `Microsoft.Playwright`, so it needs no PowerShell and always installs the browser revisions the
  pinned binding expects — unlike `npx playwright install`.
- **No `wasm-tools` workload needed**: the default build uses the Mono **interpreter**. AOT is opt-in
  via `-p:RaskWasmAot=true`, which this skill does not use.

## Build

```bash
dotnet build src/Rask.Site -c Debug -m:1
```

Serially (`-m:1`): the WASM asset pipeline races under parallel builds. Clean build = 0 warnings.

## Run (agent path)

1. **Launch it in the background** (check the port is free first):

   ```bash
   lsof -ti :5050 && echo "BUSY — see Gotchas" || echo "free"
   dotnet run --project src/Rask.Site -c Debug --no-build \
     -- --urls http://localhost:5050 > /tmp/rask-site.log 2>&1 &
   ```

2. **Wait for it** — the shell only, which is all a static host serves:

   ```bash
   for i in $(seq 1 30); do curl -sf http://localhost:5050/ -o /dev/null && { echo "UP (${i}s)"; break; }; sleep 1; done
   ```

   Do **not** read anything into the HTML this returns. It is the boot shell; the page does not exist
   until the runtime mounts.

3. **Drive it with the C# Playwright driver.** Run it **from the skill directory** so screenshots land
   in `./screenshots/`:

   ```bash
   cd .claude/skills/run-rask
   dotnet run driver.cs all          # screenshots landing/todos/routing + toggles a todo client-side
   ```

   Screenshots land in `.claude/skills/run-rask/screenshots/`. **Open one** (e.g.
   `todos-toggled.png` shows the flipped checkbox) to confirm the render is real.

   Driver commands: `shots` (screenshots only, the default), `todos` (interactive proof only), `all`.
   Point it at another port with a second arg: `dotnet run driver.cs all http://localhost:5051`.

4. **Stop it** when done:

   ```bash
   lsof -ti :5050 | xargs kill
   ```

## Run (human path)

```bash
dotnet run --project src/Rask.Site
```

Open the printed URL in a browser. Useless for automation — it blocks the terminal and opens nothing
headlessly. Ctrl-C to stop.

## Both widths, every surface (`survey.cs`)

`driver.cs` shoots three pages at 1280 only. That is half the site, and it is the half that never
breaks: the showcase is read on a phone, and **nothing in this repo checked a phone width** until
these were added — which is how a hero that overflowed 390px by 142px, guide cards that collapsed to
16px, and an install command clipped mid-URL all shipped green.

```bash
cd .claude/skills/run-rask
dotnet run survey.cs http://127.0.0.1:PORT before      # ./screenshots/before/
```

It walks the landing page, `/docs`, and three sidebar routes at **1280x900 and 390x844**, writing
full-page shots per width and printing two checks per shot:

- `page-overflow` — the document wider than the viewport. Anything but `ok` is a defect: it scrolls
  the whole page sideways and shrinks every element to match.
- `clipped=[...]` — containers whose content exceeds their box while `overflow-x` is `visible`, i.e.
  text that is lost rather than scrollable.

**Read the numbers, not the screenshot.** A clipped box and a scrolling one are pixel-identical when
the scrollbar is an overlay — the install command looked the same before and after it became
reachable.

## One route, in detail (`navprobe.cs`)

When `survey.cs` reports an overflow, this names the element responsible at 390px:

```bash
dotnet run navprobe.cs http://127.0.0.1:PORT Forms    # or "-" to stay on /docs
```

It reaches the route the way a reader does (drawer, filter, click — deep links 404 under
`WasmAppHost`) and lists only elements that **genuinely widen the document**: wider than the viewport
*and* with no scrolling ancestor. A `<pre>` inside `overflow-x: auto` is not a defect and is filtered
out; the same `<pre>` in a plain `<div>` is what you are looking for.

The usual culprit is an automatic minimum size. A grid or flex item's `min-width` defaults to `auto`
— its **min-content** width — so one `white-space: pre` block sets the whole track, and
`overflow-x-auto` on that block does not help: it makes the block scroll, it does not shrink a track
asking to be 510px wide. The fix is `min-w-0` on the item, repeated at **every** level of the chain.

## Theme + hydration probes (`themeprobe.cs`, `hydrateprobe.cs`)

Two questions arithmetic cannot answer, because both are about what a browser does with the shipped
sheets.

```bash
dotnet run themeprobe.cs   http://127.0.0.1:PORT       # theme: OS default, per-theme corrections, AA
dotnet run hydrateprobe.cs http://127.0.0.1:PORT /     # prerender -> hydrate: what flickers
```

`themeprobe.cs` loads the page under `prefers-color-scheme` light AND dark and prints whether
`data-theme` is absent (it must be — the absence is what follows the OS), which palette painted, and
the measured contrast of each `--color-ui-*` token against the ground it sits on. Then it picks
`valentine`, `retro`, `dark` and `luxury` in turn and re-measures, which is the only way to confirm the
kit's per-theme corrections in `@layer rask` actually win the cascade — they correct tokens the app's
own `@theme` declares at `:root` from a *different* `<link>`, same specificity either way.

`hydrateprobe.cs` needs the **published** bundle, not `dotnet run`: `dotnet publish` prerenders each
route to real HTML, so the handover it measures does not exist in the dev host.

```bash
dotnet publish src/Rask.Site -c Release -m:1
cd src/Rask.Site/bin/Release/net10.0-browser/publish/wwwroot && python3 -m http.server 5090
```

It samples the document every 50ms from before the first byte of page script and prints only the frames
that CHANGED — markup length, children, height, background, text colour, font — plus every `<html>`
attribute mutation, whether `<head>` and `<body>` are still the SAME ELEMENTS afterwards, and whether
the first stylesheet `<link>` is still the same connected node.

**Both of those identity checks are the point.** A MutationObserver reports a removal for an atomic
`moveBefore()` too, and a moved `<link>` keeps its sheet applied — so "every head child was removed" is
not evidence of anything on its own. Node identity is. It is how issue #1049 was pinned to `<body>`
being *replaced* rather than morphed, while `<head>` reconciled correctly.

**A probe that waits for an `<h1>` proves nothing here.** The prerendered HTML already has one, so such
a wait is satisfied before the runtime exists and reports a clean boot it never observed. This waits for
the runtime's own `raskAfterMorph` hook and says `NEVER HYDRATED` rather than implying a verdict.

## The operator console (`dashboard-driver.cs`)

The site does not mount `Rask.Dashboard`, and no app in the repo does since `samples/` was deleted. So
the console needs a throwaway app to live in — which is also the honest test, since a scaffolded app
is what a user actually mounts it in. **`ops-app.sh` builds and launches that app**, then prints the
exact driver line:

```bash
.claude/skills/run-rask/ops-app.sh                  # ~2 min (it packs the tree); PORT=5124 to move it
cd .claude/skills/run-rask
dotnet run dashboard-driver.cs http://localhost:5123 <token>   # ops-app.sh prints the token
lsof -ti :5123 | xargs kill                         # when done
```

`ops-app.sh` packs **this working tree** to a folder feed and restores the scaffold against that, which
is the only version of this that proves anything: a scaffold restored from nuget.org shoots the
*released* console, not your change — and today it does not even restore. See the console gotchas below
for the four traps that recipe walks into.

The console is behind a policy — the scaffold gates it on the **Admin** role, which only the FIRST
account gets — so the driver registers that account with the one-time first-run token, signs in, and
then shoots all five console pages at **1280 and 390** into `screenshots/` (gitignored). The console is
built mobile-first, and the two widths are genuinely different markup — columns collapse, the leader
rules disappear — so one width proves nothing about the other.

Drop the token argument to re-run against an app that already has the account.

Each shot is checked for two kinds of overflow, and the second is the one that matters:

- `PAGE-OVERFLOW` — the document is wider than the viewport.
- `TABLE-WIDE(n)` — a **table** is wider than its container. Tables carry their own `overflow-x` as a
  backstop, so a column that failed to collapse hides its content behind an internal scrollbar while the
  page-level check stays perfectly green. That is how a request id in a log scope shipped once.

Both must read `ok` on every mobile row.

The run ends with `10 screenshots in …`, and that line is a **count the driver verified**: every file is
checked for a plausible size as it is written, and the tally is asserted at the end. Anything short of
the full set throws. That guard exists because the driver spent a release shooting *nothing* — it waited
for the host app's sign-out control (`#logout-submit`), which the scaffold has never rendered on the page
you land on after signing in, so the wait burned its timeout **before the first screenshot** and the run
"finished" over an empty directory. It now waits on the console's own shell (`div.rask-ops` and its nav),
which is what it is here to look at and does not move when the scaffold's home page changes.

### Console gotchas

- **The scaffold cannot restore from nuget.org.** `rask new` pins the last published stable, and two of
  the packages it pins — `Rask.Query` and `Rask.Auth` at `0.20.0` — were never published, so a plain
  `dotnet run --project src/Rask.Cli -- new Shop` dies on `NU1103` (issue 1044). Even once that is fixed,
  the templates it writes track the tree, so they compile against API the release does not have.
  `ops-app.sh` is the supported path.
- **The global package folder beats every feed.** `~/.nuget/packages` already holds real packages at the
  pinned version, so a local feed alone is silently ignored and you screenshot the release. `ops-app.sh`
  gives the throwaway app its own `globalPackagesFolder` — outside the project directory, because inside
  it the project's own `**/*.css` glob sweeps the packages' content files into the scoped-CSS analyzer
  and the build fails on `RASK015`. That folder is **deleted whenever the feed is repacked**: the version
  is fixed at whatever `rask new` pins, so a second run writes the same `0.20.0` and NuGet, finding it
  already extracted, never reads the feed again — you would edit the console, re-run, and screenshot the
  build from before your change with every check green. `RASK_OPS_NO_PACK=1` keeps both, and is only safe
  while `src/` is untouched.
- **Packing needs the generators built in Release first.** `Rask.Api.csproj` checks for
  `Rask.Api.Generators.dll` on disk and fails the pack rather than shipping a package whose consumers get
  no generated code. `ops-app.sh` builds the three generator projects before packing.
- **Signing in is not a navigation.** A component handler runs on the WebSocket and a WebSocket cannot
  write a `Set-Cookie`, so Rask parks the sign-in and the browser redeems a one-shot ticket at
  `/_rask/auth/redeem`. The URL leaves `/login` *before* the cookie exists, so anything that waits on the
  URL races straight back to `/login`. The driver waits for that redeem response instead.

## Gotchas

- **`curl` sees a dead page.** The site only becomes a page once the WASM runtime boots. Any "does
  this render / does clicking work?" question must go through `driver.cs`, not curl.
- **A deep link 404s under `dotnet run`.** `WasmAppHost` serves files and installs no SPA fallback, so
  `http://localhost:5050/docs/todos` is a 404 — the route exists only inside the booted app. The
  driver therefore loads `/docs` and navigates through the sidebar, and so must you. The *published*
  bundle behaves differently: GitHub Pages answers an unknown path with `404.html`, which is the boot
  shell, and the app then routes on its own.
- **First load is slow.** A cold boot downloads the whole runtime; the driver's timeouts are 60s for
  that reason. A "timed out waiting for the sidebar" almost always means the boot failed, not that it
  was slow — check `/tmp/rask-site.log` and the browser console.
- **`/counter` from the README is NOT a live route.** It's a doc snippet. Real routes come from the
  sidebar under `/docs`. Hitting a bogus path renders the app's own "Page not found" — don't mistake
  it for a working page.
- **Port 5050 is shared.** If `lsof -ti :5050` shows it busy, a *different* build may be answering
  your requests and your driver will silently test the wrong app. Launch on another port and pass it
  to the driver.
- **The driver is a file-based app, so two `#:property` lines are load-bearing.** `#:package
  Microsoft.Playwright` is deliberately **unversioned** — the repo enforces Central Package
  Management, which *forbids* a version on the reference and supplies it itself; adding `@1.61.0`
  fails with NU1008. And `JsonSerializerIsReflectionEnabledByDefault=true` is required because
  file-based apps disable reflection-based System.Text.Json by default, which Playwright's transport
  needs (without it: `Reflection-based serialization has been disabled`).

## Troubleshooting

- `error NU1008: … cannot define a value for Version: Microsoft.Playwright` → you added a version to
  the `#:package` directive; remove it (CPM owns the version — see Gotchas).
- `Reflection-based serialization has been disabled` on `Playwright.CreateAsync()` → the
  `#:property JsonSerializerIsReflectionEnabledByDefault=true` line is missing from `driver.cs`.
- Driver times out waiting for `.side-nav a.side-nav-link` → the app isn't up on the base URL's port,
  or the runtime failed to boot. Re-check step 1's `lsof` and step 2's `curl`, then load the URL in a
  real browser and read the console.
- `dotnet run --project …` exits immediately with an address-in-use bind error → 5050 is taken; pick
  another port or kill the holder with `lsof -ti :5050 | xargs kill`.
- `The console did not render at /_rask — landed on …` → either the account is not an administrator
  (only the first registration is; start from a fresh app via `ops-app.sh`), or the console's shell
  markup moved and `ConsoleShell`/`ConsoleNav` in `dashboard-driver.cs` need re-deriving from
  `DashboardLayout` / `UiShell`.
- `Sign-in never completed: no 200 from /_rask/auth/redeem …` → the message carries the auth page's own
  reason when there is one ("that email is already taken", "must be at least 8 characters").
- `A first-run token was passed but /register shows no #first-run-token field` → the app is already
  claimed; drop the token argument, or re-run `ops-app.sh` for a fresh app.
- `Expected 10 screenshots … but wrote N` / `Screenshot of … is missing or empty` → the run produced
  less than the full set. Do not read this as "the console is slow"; read it as the driver refusing to
  report a green run over an empty directory.
