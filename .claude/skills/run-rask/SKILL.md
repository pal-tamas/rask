---
name: run-rask
description: Build, launch, and drive the Rask Server showcase app (samples/Rask.Example.Server) — the server-rendered, live-over-WebSocket demo of the framework. Use to run/start/launch the app, take a screenshot, or confirm a UI change works in the real running app (not just tests). Drives it headlessly with a committed C# Playwright driver — pure .NET, no Node.
---

# Run the Rask showcase

`samples/Rask.Example.Server` is the framework's showcase: plain-C# components, **server-rendered
and live-updated over a WebSocket** (the browser posts `data-rask-on-*` events, the server
re-renders and streams back a DOM diff). It's the app to launch when you want to *see* a change.

**`curl` can only see the first server-rendered HTML — it can never observe the live loop.** To
prove interactivity you need a real browser. The committed driver `.claude/skills/run-rask/driver.cs`
does that with the repo's existing **Microsoft.Playwright** (.NET) dependency — the same version the
E2E suite uses, browsers already in the `ms-playwright` cache. It's a .NET 10 *file-based app*: **no
Node, no npm, no csproj** — just `dotnet run driver.cs`.

All paths below are relative to the repo root (the unit). Run everything from there unless told
otherwise.

## Prerequisites

- **.NET 10 SDK** — `dotnet --version` → `10.0.302` here. That's the whole toolchain; the driver
  pulls `Microsoft.Playwright` via NuGet (version comes from `Directory.Packages.props`, so the
  `#:package` directive is intentionally unversioned — Central Package Management supplies it).
- **Playwright browsers** — already present in `~/Library/Caches/ms-playwright` (installed for the
  E2E suite). If missing, the driver errors with a "browser not found" message; install with
  `dotnet run --project tests/Rask.Examples.E2E.Tests -- ...` build once then run the generated
  `playwright.ps1 install chromium`, or see `scripts/run-e2e-local.sh`.

## Build

```bash
dotnet build samples/Rask.Example.Server -c Debug -m:1
```

~3–8s. Pulls in Rask.Core, Rask.Server, Rask.Bootstrap, the generators, and the shared sample
assembly. Clean build = 0 warnings.

## Run (agent path)

1. **Launch the server in the background** (override the port explicitly; check it's free first):

   ```bash
   lsof -ti :5099 && echo "BUSY — see Gotchas" || echo "free"
   ASPNETCORE_ENVIRONMENT=Development \
     dotnet run --project samples/Rask.Example.Server -c Debug --no-build \
     --urls http://localhost:5099 > /tmp/rask-server.log 2>&1 &
   ```

2. **Wait for it, smoke it with curl** (initial HTML only):

   ```bash
   for i in $(seq 1 30); do curl -sf http://localhost:5099/ -o /dev/null && { echo "UP (${i}s)"; break; }; sleep 1; done
   curl -s http://localhost:5099/ | grep -o '<title>[^<]*</title>'   # → <title>Rask</title>
   ```

3. **Drive it with the C# Playwright driver** — screenshots + the live WebSocket round-trip. Run it
   **from the skill directory** so screenshots land in `./screenshots/`:

   ```bash
   cd .claude/skills/run-rask
   dotnet run driver.cs all          # screenshots home/todos/table/routing + toggles a todo over WS
   ```

   Expected output (this is what it printed here):

   ```
   Driving http://localhost:5099  (all)
     OK /                      200  "Guides — Rask"  -> home.png
     OK /todos                 200  "Todos — Rask"  -> todos.png
     OK /table                 200  "Data table — Rask"  -> table.png
     OK /routing-demo/about    200  "Rask — feature showcase"  -> routing-about.png
     OK /todos checkbox toggled over WS: False -> True  -> todos-toggled.png
   done.
   ```

   Screenshots land in `.claude/skills/run-rask/screenshots/`. **Open one** (e.g. `todos-toggled.png`
   shows the flipped checkbox) to confirm the render is real.

   Driver commands: `shots` (screenshots only, the default), `todos` (interactive WS proof only),
   `all`. Point it at another port with a second arg: `dotnet run driver.cs all http://localhost:5199`.

4. **Stop the server** when done:

   ```bash
   lsof -ti :5099 | xargs kill
   ```

## Run (human path)

```bash
dotnet run --project samples/Rask.Example.Server
```

Serves on http://localhost:5099 (from `Properties/launchSettings.json`); open it in a browser.
Useless for automation — it blocks the terminal and opens nothing headlessly. Ctrl-C to stop.

## Gotchas

- **`curl` sees a dead page.** The showcase only becomes interactive once the browser opens the
  WebSocket. Any "does clicking work?" question must go through `driver.cs`, not curl.
- **`/counter` from the README is NOT a live route.** It's a doc snippet. The showcase's real routes
  come from the sidebar: `/` (guides), `/todos`, `/table`, `/routing-demo/about`, `/server-pwa`, and
  the device-API demos (`/serial`, `/usb`, `/bluetooth`, `/wake-lock`, …). Hitting a bogus path
  returns a styled 200 "Page not found" — don't mistake it for a working page.
- **Port 5099 is shared and hardcoded.** It's the same port the E2E gate (`scripts/run-e2e-local.sh`)
  and every other worktree use. If `lsof -ti :5099` shows it busy, a *different* build is answering
  your requests and your driver will silently test the wrong app. Launch on another port
  (`--urls http://localhost:5199`) and pass `dotnet run driver.cs all http://localhost:5199`. The
  pre-push hook also runs E2E on 5099, so a server you left running there will block a push.
- **The driver is a file-based app, so two `#:property` lines are load-bearing.** `#:package
  Microsoft.Playwright` is deliberately **unversioned** — the repo enforces Central Package
  Management, which *forbids* a version on the reference and supplies 1.61.0 itself; adding `@1.61.0`
  fails with NU1008. And `JsonSerializerIsReflectionEnabledByDefault=true` is required because
  file-based apps disable reflection-based System.Text.Json by default, which Playwright's transport
  needs (without it: `Reflection-based serialization has been disabled`).
- **Native samples don't build here.** `Rask.Example.Native{,.Server}` multi-target
  `net10.0-ios;net10.0-android` and are excluded from `Rask.slnx`; without the mobile workloads they
  won't restore. This skill is the *web* showcase only.

## Troubleshooting

- `error NU1008: … cannot define a value for Version: Microsoft.Playwright` → you added a version to
  the `#:package` directive; remove it (CPM owns the version — see Gotchas).
- `Reflection-based serialization has been disabled` on `Playwright.CreateAsync()` → the
  `#:property JsonSerializerIsReflectionEnabledByDefault=true` line is missing from `driver.cs`.
- Driver hangs on `WaitForFunctionAsync` / times out → the server isn't up on the base URL's port, or
  another worktree grabbed 5099. Re-check step 1's `lsof` and `curl`.
- `dotnet run --project …` exits immediately with an address-in-use bind error → 5099 is taken; pick
  another port (see Gotchas) or kill the holder with `lsof -ti :5099 | xargs kill`.
