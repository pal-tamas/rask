# scripts/flux/Flux — the Flux UI harness

Measures Flux UI's live docs (fluxui.dev) and holds Rask.Ui to them: the `flux-component` skill's tool. One
C# console app on Microsoft.Playwright — the version `Directory.Packages.props` pins for the E2E suites — so
neither a user nor a maintainer nor CI needs Node or npm. The runtime bundles a parity page gets are built with
the native esbuild the build already caches (`ResolveTypeScriptToolTask`).

```bash
dotnet run --project scripts/flux/Flux -- install chromium         # once per Playwright bump (or webkit, --with-deps)
dotnet run --project scripts/flux/Flux -- parity button             # a component against Flux's docs page
dotnet run --project scripts/flux/Flux -- measure /tmp/flux components/button
dotnet run --project scripts/flux/Flux --                           # every command, one line each
```

Each command is named after the script it was (`parity.mjs` is `parity`) and takes the same arguments, prints
the same report and writes the same files (`artifacts/flux-parity/…`, `flux.snapshot.json`, `flux.lock.json`). A
check run many times in a row (`scripts/flux/parity-all.sh`) builds once and runs
`dotnet scripts/flux/Flux/bin/Release/net10.0/flux.dll <command>`: concurrent `dotnet run`s would race on one `obj/`.

## Layout

- `Program.cs` — finds every command by its attribute and runs the one asked for.
- `Commands/` — one class per command.
- `Preparations/` — the states `parity --prepare <name>` drives both pages into (`command-open`, `empty-state`,
  `composer-grown`, `picker-open`).
- `Lib/` — what the commands share:
  - `Measure` (+ `PageScripts`, the functions that run in the page): how an example is measured, as loaded, in
    light and dark, with its forced hover/active/focus-visible states; layouts' demos.
  - `ParityComparer`: one Rask example against its Flux twin, by marker — the rules parity reports by.
  - `OpenList` + `OpenKind`: a control's OPEN list (select, autocomplete, pillbox) and its `--live` walks.
  - `Placement`: where an open panel lands when held at the viewport's edges.
  - `Departed`, `Pressables`, `Compare`: the owner-approved departures and the value rules every comparison shares.
  - `Runtime` (esbuild bundles of `src/Rask.Core/Resources`), `Screen`/`View` (desktop vs phone, taps),
    `Browsers`, `Eval` (page functions with JSON in and out), `Js` (JavaScript's rounding, `String(number)` and
    `JSON.stringify`, so reports and files read byte for byte as the scripts' did), `Npm` (registry tarballs, for
    the vendoring commands), `Repo`, `Args`.

## Adding a check

One file, nothing else to register:

```csharp
namespace Rask.Flux.Commands;

[FluxCommand("parity-menu", "a menu's open popup against Flux's")]
public sealed class ParityMenu : IFluxCommand
{
    public async Task<int> Run(string[] args)
    {
        var arg = new Args(args);
        var view = View.Of(arg);                       // --view 390x844
        var browser = await Browsers.Chromium();
        // … Measure / OpenList / Placement / ParityComparer …
        await browser.CloseAsync();
        return 0;                                      // the exit code: 1 on any difference
    }
}
```

A state `parity` should measure the component in is an `IParityPreparation` marked
`[ParityPreparation("name")]` in `Preparations/`; a control that opens a list is an `OpenKind` handed to
`OpenList.Run(kind, args)`. In-page code is a JavaScript function expression in a C# raw string, run with
`page.Run(fn, arg)` (or on an element handle / locator): the argument crosses as JSON and the answer comes back as
a `JsonNode`. Changing `Lib/Measure.cs`, `Lib/PageScripts.cs` or anything `Sync.MeasuringCode` names retakes the
Flux lock on the next `upstream.yml` run.

The project keeps the repository's build rules (warnings as errors, central package versions); it is in no
solution and is never packed.
