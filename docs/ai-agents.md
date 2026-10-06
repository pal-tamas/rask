# Building Rask apps with AI assistants

Rask ships first-class guidance for AI coding tools, so an assistant can scaffold and extend a
Rask app correctly without you re-explaining the conventions.

## What's included

- **`llms.txt`** (repo root) — the index AI tools read first. One dense entry per guide carries the rules
  that make Rask code compile (the chain not `new`, the children indexer, required steps versus optional
  setters, the root rendering into `<body>`, routing and lifecycle, scoped CSS/TypeScript, callbacks, forms,
  auth) and links the guide behind each. A generated project ships no `AGENTS.md` of its own — point your
  assistant at `llms.txt`.
- **`AGENTS.md`** (repo root) — for an assistant working **on Rask itself**: the gate, the repo's
  workflows, how a change lands. It is not app guidance.
- **The published set on rask.sh**, for an assistant that reads the web rather than a checkout:
  [`https://rask.sh/llms.txt`](https://rask.sh/llms.txt) indexes every guide with a one-line summary,
  [`https://rask.sh/llms-full.txt`](https://rask.sh/llms-full.txt) is every app-building guide in one
  file, and each guide's Markdown sits at its page's address plus `.md` — for example
  [`https://rask.sh/docs/guides/cqrs.md`](https://rask.sh/docs/guides/cqrs.md). They are generated from the
  same `docs/` at every site publish, with the links rewritten to resolve on the site.
- **The `docs/` set** — a task guide for each subsystem (getting-started, elements & the DSL, routing,
  lifecycle, composition (every element event as MDN's type, keyboard values as the generated `Keys`/`Codes`
  constants), subscriptions, forms, js-interop, browser APIs, authentication, data access, HTTP & files,
  PWA, CQRS, diagnostics, testing, accessibility — typed `Aria*` steps and `AriaRole` constants generated from
  the WAI-ARIA spec, so an assistant writes `.AriaExpanded(open)` rather than guessing at `.Aria("expanded", "true")`
  strings, … — the full curated list is in the on-site guides index) plus the
  Tailwind, compiled at build time (`docs/tailwind.md`: utilities scanned from your own C# source, zero-JS
  interactivity, typed utility classes). Each guide embeds its examples as live demos, so the source
  a user reads on GitHub and the running showcase stay in lockstep.

## How to use it

1. Scaffold: `rask new MyApp`.
2. Point your assistant at Rask's `llms.txt` — the repo-root file, or `https://rask.sh/llms.txt` if it reads the web.
3. Ask for features in plain language — the assistant follows the conventions and links to
   `docs/diagnostics.md` when it hits a `RASKxxx` compile diagnostic.
4. Ship it: `rask deploy --host user@box --domain app.example.com` builds and runs it on a single
   host over SSH with automatic HTTPS.

## Keeping it accurate

The repo-root `llms.txt` and this guide are part of the public API surface: when a user-facing
behavior changes, they're updated in the same commit (see `docs/development-workflow.md`). GitHub is
the single source of truth — these files are committed, not local-only.
