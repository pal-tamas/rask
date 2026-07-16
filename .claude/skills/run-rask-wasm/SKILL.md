---
name: run-rask-wasm
description: Build, launch, and drive the Rask WASM showcase (samples/Rask.Example.Wasm, served by Rask.Example.Wasm.Host) — the same components running client-side on WebAssembly. Use to run/start/launch the WASM app, take a screenshot, or confirm a change works in the real browser-WASM runtime (not just tests). Drives it headlessly with a committed C# Playwright driver — pure .NET, no Node.
---

# Run the Rask WASM showcase

`samples/Rask.Example.Wasm` is the *same* showcase components as the Server host, but running
**client-side on WebAssembly**: `Rask.Example.Wasm.Host` (an ASP.NET host using `Rask.Wasm.Hosting`)
serves a published AppBundle, the browser downloads `dotnet.wasm` + assemblies, boots the Mono
runtime, and renders + handles events **locally via JSImport/JSExport — there is no server
WebSocket.**

**`curl` is even more useless here than for the Server host.** It sees only the shell HTML, and the
framework assets are fingerprinted and resolved through the page's import map — so `/_framework/dotnet.js`
even **404s on a direct GET**. The app only exists once a real browser boots the runtime. The committed
driver `.claude/skills/run-rask-wasm/driver.cs` does that with the repo's existing Microsoft.Playwright
(.NET) dependency — a .NET 10 file-based app, **no Node, no npm, no csproj**.

All paths below are relative to the repo root (the unit).

## Prerequisites

- **.NET 10 SDK** — `dotnet --version` → `10.0.302` here.
- **Playwright browsers** — already in `~/Library/Caches/ms-playwright` (installed for the E2E suite).
- No `wasm-tools` workload needed: the default build uses the Mono **interpreter** (AOT is opt-in via
  `-p:RaskWasmAot=true`, which this skill does not use).

## Build

Build the **host** — it build-references the WASM app and stages its published `wwwroot` bundle
(building the WASM app alone does not produce something the host can serve). Build **serially**
(`-m:1`): the WASM asset pipeline races under parallel builds.

```bash
dotnet build samples/Rask.Example.Wasm.Host -c Debug -m:1
```

~18s warm (WASM compile dominates). Clean build = 0 warnings.

## Run (agent path)

1. **Launch the host** (port 5050 is its dev default; check it's free first):

   ```bash
   lsof -ti :5050 && echo "BUSY — see Gotchas" || echo "free"
   DOTNET_ENVIRONMENT=Production \
     dotnet run --project samples/Rask.Example.Wasm.Host --no-launch-profile --no-build -c Debug \
     -- --urls http://localhost:5050 > /tmp/rask-wasm.log 2>&1 &
   ```

2. **Wait for the shell** (curl only proves the host is listening — not that WASM booted):

   ```bash
   for i in $(seq 1 40); do curl -sf http://localhost:5050/ -o /dev/null && { echo "UP (${i}s)"; break; }; sleep 1; done
   curl -s http://localhost:5050/ | grep -o '<title>[^<]*</title>'   # → <title>Rask WASM</title>
   ```

3. **Drive it with the C# Playwright driver** — it boots the runtime in a real browser, screenshots,
   and proves client-side interactivity. Run **from the skill directory** so screenshots land in
   `./screenshots/`:

   ```bash
   cd .claude/skills/run-rask-wasm
   dotnet run driver.cs all
   ```

   Expected output (this is what it printed here):

   ```
   Driving http://localhost:5050  (all)  [WASM — client-side]
     OK /          200  "Guides — Rask"  -> home.png
     OK /todos     200  "Todos — Rask"  -> todos.png
     OK /table     200  "Data table — Rask"  -> table.png
     OK /todos checkbox toggled CLIENT-SIDE: False -> True  -> todos-toggled.png
   done.
   ```

   Screenshots land in `.claude/skills/run-rask-wasm/screenshots/`. **Open** `todos-toggled.png`
   (shows "2 items, 2 done" after the flip) to confirm the WASM runtime actually rendered.

   Commands: `shots` (default), `todos` (interactive only), `all`. Second arg overrides the base URL:
   `dotnet run driver.cs all http://localhost:5051`.

4. **Stop the host:**

   ```bash
   lsof -ti :5050 | xargs kill
   ```

## Run (human path)

```bash
dotnet run --project samples/Rask.Example.Wasm.Host
```

Serves on http://localhost:5050 (`Properties/launchSettings.json`); open it in a browser. First load
is slow — the browser downloads the whole WASM runtime. Ctrl-C to stop.

## Gotchas

- **No WebSocket — it's client-side WASM.** Unlike the Server host, there's no `data-rask-connecting`
  attribute and no WS round-trip. The driver's readiness signal is "the sidebar nav appeared" (the
  runtime booted and mounted the first render), and the interactive proof is a **local** re-render
  after the click, not a server round-trip.
- **`curl` can't fetch the runtime.** Framework assets are fingerprinted (`dotnet.<hash>.js`) and
  resolved via the index.html import map, so `/_framework/dotnet.js` returns **404** on a direct GET
  even though the app loads fine in a browser. Don't read that 404 as "broken."
- **Build the HOST, not just the WASM app.** `Rask.Example.Wasm.Host` sets
  `StaticWebAssetsEnabled=false` and serves the *staged/published* bundle via `Rask.Wasm.Hosting`
  (`app.UseRask<App>()`). The bundle is staged as a side effect of building the host (which
  build-references the WASM project). If you edit the WASM app and re-run with `--no-build`, you'll
  serve the stale bundle — rebuild the host.
- **Cold WASM boot is slow.** The E2E fixture allows a 180s ready timeout; the driver uses 60s waits.
  If a page looks empty in a screenshot, the runtime likely hadn't finished booting — the driver
  waits for the sidebar to avoid that, but a heavily loaded machine can still be slow.
- **Port 5050 vs 5098.** 5050 is this host's dev launchSettings port (used above); the E2E gate boots
  the same host on **5098** (`WasmExampleAppFixture`). Pick a free port and pass it to both the host
  (`--urls`) and the driver (second arg) if either is taken by another worktree.

## Troubleshooting

- Driver times out in `WaitBootedAsync` (sidebar never appears) → the WASM runtime didn't boot. Check
  `/tmp/rask-wasm.log`, confirm the host is on the port you drove, and that you **built the host**
  (not just the app) so the bundle is staged.
- Screenshot is blank / assets 404 in the browser → you ran `--no-build` after changing the WASM app
  without rebuilding the host, so the staged bundle is stale/missing. Rebuild the host.
- `dotnet build` fails with `MSB4018 … UpdateExternallyDefinedStaticWebAssets` or a fingerprinted
  `dotnet.native.*` race → you built in parallel; rebuild with `-m:1`.
