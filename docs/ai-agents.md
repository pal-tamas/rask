# Building Rask apps with AI assistants

Rask ships first-class guidance for AI coding tools, so an assistant can scaffold and extend a
Rask app correctly without you re-explaining the conventions.

## What's included

- **`AGENTS.md`** — every project created with `dotnet new rask-server | rask-wasm |
  rask-wasm-hosted` contains an `AGENTS.md` at its root. It's the cross-tool standard most AI
  coding assistants read automatically; it captures the rules that make Rask code compile
  (factories not `new`, the children indexer, factory-param props, the full-shell root,
  routing/lifecycle, scoped CSS/JS, callbacks, forms, auth).
- **`llms.txt`** (repo root) — the emerging standard index that points AI tools at the docs.
- **The `docs/` set** — task guides (getting-started, routing, lifecycle, composition, forms,
  js-interop, authentication, migration-from-blazor, diagnostics, testing).

## How to use it

1. Scaffold: `dotnet new rask-server -o MyApp`. The generated `AGENTS.md` travels with the repo.
2. Point your assistant at the project; it picks up `AGENTS.md` (and `llms.txt` if it fetches docs).
3. Ask for features in plain language — the assistant follows the conventions and links to
   `docs/diagnostics.md` when it hits a `RASKxxx` compile diagnostic.

## Keeping it accurate

The `AGENTS.md` templates and `llms.txt` are part of the public API surface: when a user-facing
behavior changes, they're updated in the same PR (see `docs/development-workflow.md`). GitHub is
the single source of truth — these files are committed, not local-only.
