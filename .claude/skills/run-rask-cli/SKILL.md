---
name: run-rask-cli
description: Build and drive the `rask` CLI (src/Rask.Cli) — the framework's scaffolding front door (rask new / generate / db / dev / deploy / info). Use to run, build, smoke-test, or exercise the CLI, or to confirm a change to `rask new` or `rask generate` actually scaffolds a real project on disk (not just that a unit test passes). Driven by a committed bash smoke script.
---

# Run / drive the `rask` CLI

`src/Rask.Cli` is the `rask` command-line tool: `rask new` scaffolds a project, `rask generate`
scaffolds pages/components/CRUD features/jobs/emails into it, and `rask info|db|dev|deploy` wrap the
.NET SDK. It ships as a .NET tool (`ToolCommandName=rask`), but from this repo you drive it from
source with `dotnet run --project src/Rask.Cli -- <args>`.

The thing worth *driving* (vs. unit-testing) is the two commands that write artifacts end-to-end —
`new` and `generate`. The committed driver **`.claude/skills/run-rask-cli/smoke.sh`** scaffolds a
project into a temp dir, generates into it, and asserts both exit codes *and* the files/plans that
appear. That's the CLI's equivalent of "take a screenshot": proof the assembled tool produces a real
project, not just that an internal method returns the right string.

All paths below are relative to the repo root (the unit).

## Prerequisites

- **.NET 10 SDK** — `dotnet --version` → `10.0.302` here. Nothing else for the smoke (it uses
  `--dry-run` for the package-adding step, so it needs no network).
- **Network** is required only for the *full* `rask generate feature` (no `--dry-run`) and for
  `rask new`'s restore — they hit nuget.org. See Gotchas.

## Build

```bash
dotnet build src/Rask.Cli -c Debug -m:1
```

Sub-second warm. Clean build = 0 warnings.

## Run (agent path)

Run the smoke driver — it builds the CLI, then drives `info`, `new`, `generate`, and the error paths,
printing PASS/FAIL per check and exiting non-zero on any failure:

```bash
.claude/skills/run-rask-cli/smoke.sh
# or skip the rebuild if you just built:
RASK_CLI_NO_BUILD=1 .claude/skills/run-rask-cli/smoke.sh
```

Expected tail (this is what it printed here):

```
==> Scaffold a new server project
  PASS  new Shop  (exit 0)
  PASS  file: Shop/Shop.csproj
  ...
==> Generate a CRUD feature (dry-run: hermetic, shows the plan, no nuget)
  PASS  generate feature --dry-run (CRUD plan)
==> Error paths (must exit 1 with a helpful message)
  PASS  generate outside a project  (exit 1)
  PASS  unknown command  (exit 1)
  PASS  bad field type  (exit 1)

==> 12 passed, 0 failed.
```

The driver scaffolds into a `mktemp -d` dir and cleans up on exit — it leaves nothing in the repo.

## Direct invocation (drive one command by hand)

From source, everything after `--` is passed to `rask`:

```bash
dotnet run --project src/Rask.Cli --no-build -- info
dotnet run --project src/Rask.Cli --no-build -- new Shop --template server --output /tmp/Shop
# generate resolves the project by walking UP for a single .csproj, so run it INSIDE the project:
( cd /tmp/Shop && dotnet run --project "$OLDPWD/src/Rask.Cli" --no-build -- \
    generate feature Product Name:string Price:decimal --dry-run )
```

`--dry-run` prints each file it *would* write (and adds no packages, touches no network) — use it to
inspect scaffolding output hermetically. Drop it to actually write the files.

## Run (human path)

Packaged as a global tool (`dotnet tool install -g Rask.Cli` → `rask new …`). Not exercised here — no
published package in this container — so treat the `dotnet run --project … --` form above as the
verified path.

## Test

```bash
dotnet test tests/Rask.Cli.Tests -c Debug
```

325 tests, ~real-fast. This is the unit sanity check; the smoke driver is what proves the
end-to-end scaffolding.

## Gotchas

- **`generate` resolves the target project by walking UP the directory tree for a *single* `.csproj`.**
  Run it from inside the project dir. From a dir with no project it exits 1 (`Couldn't find a single
  .csproj …`); worse, from a dir that happens to have a *stray* `.csproj` above it, it silently
  scaffolds into that project's namespace — running `generate page Foo` from `/tmp` here produced a
  `namespace Rask.Wasm.Hosting.Features.Foo` because it found a csproj up the tree. Always `cd` into
  the project first.
- **Validation order: project first, fields second.** `generate feature Bad Name:wobble` reports
  `Couldn't find a single .csproj` (not the bad type) when run outside a project — the type error
  (`Unknown field type 'wobble'. Supported: …`) only surfaces once a project is found. That's why the
  smoke runs the bad-field check *inside* the scaffolded Shop project.
- **The full `generate feature` mutates the `.csproj` and hits nuget.org** to add EF Core + SQLite,
  Rask.Cqrs, Rask.Data. That's why the smoke uses `--dry-run` for it. A real run needs network and
  will edit the project file — don't run it against a repo you don't want touched.
- **Mind the `--`.** `dotnet run --project src/Rask.Cli -- <rask args>` — without the `--`, `dotnet`
  tries to parse `new`/`generate`/flags as *its own* arguments.
- **Version looks like `0.18.1-alpha.0.16`** — MinVer-derived from git height, not a hardcoded
  string. `rask --version` and `rask info` both report it.

## Troubleshooting

- `Couldn't find a single .csproj at or above '<dir>'. Run this inside a Rask project.` → you ran
  `generate` from outside a project; `cd` into the scaffolded app first.
- `dotnet` errors with `Unrecognized command or argument 'new'` (or similar) → you dropped the `--`
  separating dotnet's args from rask's.
- The smoke's `generate feature --dry-run` check fails with a network/package error → you removed
  `--dry-run`; the real feature generate needs nuget.org and writes to the `.csproj`.
