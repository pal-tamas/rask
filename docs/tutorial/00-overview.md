# Tutorial: build and ship a whole product

This is the hands-on companion to [Getting started](../getting-started.md). Where that guide teaches you
the UI (components, state, routing), this tutorial takes you the rest of the way — from an empty folder
to a **deployed, database-backed product that one person runs on one server**. That is the promise of
[the .NET One Person Framework](../one-person-framework.md), and this is the walk-through that proves it.

We build a small online shop — **Shop** — and grow it one chapter at a time. Every step is a real command
you type and real code the `rask` CLI writes for you. Nothing here is pseudo-code.

> **Want to try the ideas before installing anything?** The
> [playground's guided tutorial](../playground.md#the-guided-tutorial) teaches components, state, forms
> and then EF Core CRUD — running real SQLite in your browser tab, with nothing to set up. It covers the
> code; this tutorial covers the product: the CLI, migrations, jobs, email, and shipping it.

## What you'll build

By the last chapter, Shop has:

- a **Products** catalog with full create / read / update / delete pages, backed by SQLite through EF Core;
- an **Orders** feature;
- a **login** so only signed-in staff can edit the catalog;
- a **background job** that runs work off the request thread;
- **transactional email** (an order receipt) whose body is a Rask component;
- a **cache** in front of the catalog;
- **domain events** delivered through a durable outbox;
- **production-grade SQLite** — WAL, a busy-timeout, snapshots, and continuous off-box backup;
- **push notifications** sent from your own server on your own keys;
- an **ops page** that shows every background worker's state;
- and finally, a **one-command deploy** to a single server behind automatic HTTPS.

Every one of those is a first-party Rask **pillar** — a thin, opinionated package that rides on the app's
own SQLite database. No Redis, no message broker, no managed queue, no second server. One box runs the
whole product.

## The chapters

| # | Chapter | Pillar | You'll build |
|---|---------|--------|-----------|
| 1 | [Scaffold the app](01-scaffold.md) | CLI · Auth | `rask new Shop --auth` |
| 2 | [Your first feature](02-first-feature.md) | Data · CQRS · SQLite | a `Product` slice · `rask db` |
| 3 | [A second feature + locking it down](03-orders-and-auth.md) | Auth | an `Order` slice on the same database |
| 4 | [Background jobs](04-background-jobs.md) | Jobs | an `IBackgroundJob` + handler |
| 5 | [Transactional email](05-email.md) | Mail | an email component + `IMail` |
| 6 | [Caching the catalog](06-cache.md) | Cache | a cached read accessor |
| 7 | [Domain events + the outbox](07-outbox-events.md) | Outbox | `IOutboxEvent`s + a handler |
| 8 | [Production SQLite](08-production-sqlite.md) | SQLite | `UseRaskSqlite()` · snapshots · Litestream |
| 9 | [Push notifications](09-web-push.md) | Web Push · PWA | `VapidKeys.Generate()` · `IWebPush` |
| 10 | [Watching it run](10-ops.md) | Observability | an `/ops` page over every pillar's table |
| 11 | [Deploy to one box](11-deploy.md) | Deploy | `rask deploy --host … --domain …` |

Read them in order — each chapter builds on the app the previous one left behind. Every chapter ends with
a **Verify** section (how to confirm it works) and a **Learn more** link to that pillar's reference doc.

One command in Chapter 1 wires every pillar in; each chapter then teaches you what one of them is *for* and
how to use it. If you'd rather add them one at a time, every flag works on its own —
`rask new Shop --no-push --no-ops` gives you everything except those two.

**Want to read ahead?** [`samples/Rask.Example.Shop`](../../samples/Rask.Example.Shop) is the finished app,
committed and runnable — the output of this tutorial's commands, with browser tests that prove each pillar
actually runs.

## Before you start

You need the **.NET 10 SDK** and the **`rask` CLI**. One command gets you both:

```bash
curl -sSL https://rask.sh/rask.sh | sh
```

It installs the SDK only if you don't already have one, adds the `rask` command, and finishes by
running `rask doctor` so you can see where you stand. Everything lands under `$HOME` — no `sudo`.
On Windows, use `irm https://rask.sh/rask.ps1 | iex`.

Already set up? `dotnet --version` should print `10.0` or newer, and
`dotnet tool install -g Rask.Cli` (or `dotnet tool update -g Rask.Cli`) is all you need. Full
detail: [Installing Rask](../installation.md).

This tutorial assumes you're comfortable with C#. It does **not** assume you know EF Core, CQRS, or Rask —
each idea is introduced where it first appears. If you've never built a Rask UI, skim
[Getting started](../getting-started.md) first; we won't re-teach components here.

Ready? → **[Chapter 1: Scaffold the app](01-scaffold.md)**
