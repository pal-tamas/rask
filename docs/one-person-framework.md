# The .NET One Person Framework

Rask is built on one conviction: **a single developer should be able to build, run, and ship a complete
product — the UI, the data, the auth, the background work, and the deployment — from one C# codebase on
one server.** No PaaS to rent, no stack of services to assemble and glue, no second language to
context-switch into. That is what "One Person Framework" means here, and every design decision serves it.

This page is the doctrine. The [getting-started guide](getting-started.md) is the hands-on UI path, the
[zero-to-deploy tutorial](tutorial/00-overview.md) builds a whole product end to end, and the
[docs index](README.md) is the full map.

## The problem it removes

Shipping a product the conventional way means assembling a stack: a frontend framework in one language, a
backend in another, a managed database, a queue for background jobs, a cache, a blob store, and a
deployment pipeline to tie them together. Each piece is rented, integrated, monitored, and paid for. For a
team that division of labor pays off. For **one person**, it is mostly overhead — the integration seams,
the context-switching, and the monthly bill for capacity you don't yet need.

Rask's answer is to collapse the stack. One language (C#), one codebase, one database file, one server.

## One codebase, every surface

You write the UI once, as plain C# components that return a tree of HTML from `Render()`. The *same*
component code runs on three hosts — you pick per project, not per component:

- **Server** — rendered server-side, updated live over a WebSocket with a minimal diff.
- **WASM** — the same component running fully client-side in the browser (and installable as an offline PWA).
- **Native** — the same component shipped as a real iOS/Android app via `Rask.Native` *(preview)*.

Behind the UI, features are **vertical slices**: [`Rask.Cqrs`](cqrs.md) gives you source-generated
commands/queries/notifications, and [`Rask.Data`](data.md) gives every aggregate a base with identity,
audit stamps, soft delete, optimistic concurrency, and domain events — driven by EF Core interceptors, not
boilerplate you copy into each feature. You don't wire a mediator or write a repository; you describe the
slice and the framework assembles it.

## SQLite-first: one server, no PaaS

Rask treats **SQLite as the production database**, not a toy or a test double. A single database file on
the server's local disk, configured for real concurrent web traffic (WAL journaling, a busy-timeout,
enforced foreign keys) and **continuously backed up** by streaming its write-ahead log to object storage —
plus scheduled full-file snapshots as a second line of defence.

The payoff: **one box runs the whole product.** No managed database to provision and pay for, no network
hop to a database tier, no separate cache or queue service — because everything stateful can ride the same
SQLite database. When you outgrow one box, the door to a client-server database is open; most solo products
never need to walk through it.

That is measured, not asserted: on one laptop, one file sustains **~99,000 operations/second of realistic
90/10 read-write traffic at a p99 of 10 ms**, and with WAL a writer hammering the same file costs concurrent
readers under half their throughput — the same reads without WAL collapse to roughly 1% of it. The full
load-test tables, including where the defaults *don't* save you, are in
**[Load-test numbers](sqlite.md#load-test-numbers)**.

The full reasoning — why WAL, why a busy-timeout, why single-writer is fine for a web app, and how the
continuous backup works — is in **[Why one server, no PaaS](sqlite.md)**.

## The batteries

Everything a solo developer needs to go from empty folder to shipped, in the box:

| Battery | What it does |
|---------|--------------|
| **[The `rask` CLI](cli.md)** | `rask new` (scaffold), `rask dev` (watch + hot reload), `rask generate` (page/component/feature). The front door. |
| **[`rask generate feature`](cli.md)** | One command emits a full CQRS + EF Core CRUD vertical slice — encapsulated entity, value objects, validation, list/create/edit pages (and, with `--tests`, a test project) — and wires the DI into `Program.cs`. |
| **[`Rask.Data`](data.md)** | `Entity<TId>` + EF interceptors: audit stamps, transparent soft delete, optimistic concurrency, domain events. |
| **[`Rask.Cqrs`](cqrs.md)** | Source-generated, reflection-free CQRS/mediator — trim/AOT-safe, zero runtime scanning. |
| **[Production SQLite](sqlite.md)** | WAL + busy-timeout pragmas, continuous backup (Litestream), scheduled snapshots. |
| **[Auth](authentication.md)** | Cookie & JWT login/session scaffolding in the templates. |
| **[PWA & native](pwa.md)** | Installable offline apps and real iOS/Android from the same components. |
| **[Deploy](deployment.md)** | `rask deploy` takes a bare VPS to a live HTTPS site — installs Docker, a non-root deploy login, a firewall and SSH hardening, then builds on the box and swaps in with zero downtime. No SSH session of your own required. |

## DB-backed by default

The through-line for everything stateful: **it rides the app's own SQLite database.** No external broker,
no Redis, no separate infrastructure to stand up for a hello-world. Background jobs, the transactional
outbox, cache, and mail are all designed to persist to the same database the app already has — so adding
one is a package reference, not a new service to operate.

See the **[roadmap](roadmap.md)** for what's shipped and what's next along this line.

## Where to go next

- **[Tutorial: zero to deploy](tutorial/00-overview.md)** — build a whole product, one pillar per chapter.
- **[Getting started](getting-started.md)** — zero to a running, routed, interactive app.
- **[The `rask` CLI](cli.md)** — scaffold and run.
- **[Why one server, no PaaS](sqlite.md#why-one-server-no-paas)** — the SQLite production story.
- **[Roadmap](roadmap.md)** — the DB-backed pillars, shipped vs planned.
