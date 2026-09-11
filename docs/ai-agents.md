# Building Rask apps with AI assistants

Rask ships first-class guidance for AI coding tools, so an assistant can scaffold and extend a
Rask app correctly without you re-explaining the conventions.

## What's included

- **`AGENTS.md`** (repo root) — the AI guidance that ships with the Rask repository. It's the
  cross-tool standard most AI coding assistants read automatically; it captures the rules that make
  Rask code compile (the chain not `new`, the children indexer, step-vs-setter props, the full-shell
  root, routing/lifecycle, scoped CSS/TypeScript, callbacks, forms, auth). Generated projects no longer ship
  their own `AGENTS.md` — point your assistant at this repo-root guidance (and `llms.txt`).
- **`llms.txt`** (repo root) — the emerging standard index that points AI tools at the docs.
- **The published set on rask.sh**, for an assistant that reads the web rather than a checkout:
  [`https://rask.sh/llms.txt`](https://rask.sh/llms.txt) indexes every guide with a one-line summary,
  [`https://rask.sh/llms-full.txt`](https://rask.sh/llms-full.txt) is every app-building guide in one
  file, and each guide's Markdown sits at its page's address plus `.md` — for example
  [`https://rask.sh/docs/guides/cqrs.md`](https://rask.sh/docs/guides/cqrs.md). They are generated from the
  same `docs/` at every site publish, with the links rewritten to resolve on the site.
- **The `docs/` set** — a task guide for each subsystem (getting-started, elements & the DSL, routing,
  lifecycle, composition, forms, js-interop, browser APIs, authentication, data access, HTTP & files,
  PWA, CQRS, diagnostics, testing, … — the full curated list is in the on-site guides index) plus the
  Tailwind, compiled at build time (`docs/tailwind.md`: utilities scanned from your own C# source, zero-JS
  interactivity, typed utility classes). Each guide embeds its examples as live demos, so the source
  a user reads on GitHub and the running showcase stay in lockstep.

## How to use it

1. Scaffold: `rask new MyApp`.
2. Point your assistant at Rask's repo-root `AGENTS.md` (and `llms.txt` if it fetches docs).
3. Ask for features in plain language — the assistant follows the conventions and links to
   `docs/diagnostics.md` when it hits a `RASKxxx` compile diagnostic.
4. Ship it: `rask deploy --host user@box --domain app.example.com` builds and runs it on a single
   host over SSH with automatic HTTPS.

## Keeping it accurate

The repo-root `AGENTS.md` and `llms.txt` are part of the public API surface: when a user-facing
behavior changes, they're updated in the same PR (see `docs/development-workflow.md`). GitHub is
the single source of truth — these files are committed, not local-only.
