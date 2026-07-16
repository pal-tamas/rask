# Building Rask apps with AI assistants

Rask ships first-class guidance for AI coding tools, so an assistant can scaffold and extend a
Rask app correctly without you re-explaining the conventions.

## What's included

- **`AGENTS.md`** (repo root) — the AI guidance that ships with the Rask repository. It's the
  cross-tool standard most AI coding assistants read automatically; it captures the rules that make
  Rask code compile (factories not `new`, the children indexer, factory-param props, the full-shell
  root, routing/lifecycle, scoped CSS/JS, callbacks, forms, auth). Generated projects no longer ship
  their own `AGENTS.md` — point your assistant at this repo-root guidance (and `llms.txt`).
- **`llms.txt`** (repo root) — the emerging standard index that points AI tools at the docs.
- **The `docs/` set** — a task guide for each subsystem (getting-started, elements & the DSL, routing,
  lifecycle, composition, forms, js-interop, browser APIs, authentication, data access, HTTP & files,
  PWA, native mobile, CQRS, diagnostics, testing, … — the full curated list is in the on-site guides index) plus the
  optional `Rask.Bootstrap` reference (`docs/bootstrap.md`: typed Bootstrap 5.3 components, zero-JS
  interactivity, typed utility classes). Each guide embeds its examples as live demos, so the source
  a user reads on GitHub and the running showcase stay in lockstep.

## How to use it

1. Scaffold: `rask new MyApp`.
2. Point your assistant at Rask's repo-root `AGENTS.md` (and `llms.txt` if it fetches docs).
3. Ask for features in plain language — the assistant follows the conventions and links to
   `docs/diagnostics.md` when it hits a `RASKxxx` compile diagnostic.

## Keeping it accurate

The repo-root `AGENTS.md` and `llms.txt` are part of the public API surface: when a user-facing
behavior changes, they're updated in the same PR (see `docs/development-workflow.md`). GitHub is
the single source of truth — these files are committed, not local-only.
