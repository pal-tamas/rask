# Tutorial: build and ship a whole product

This is the hands-on companion to [Getting started](../getting-started.md). Where that guide teaches you
the UI (components, state, routing), this tutorial takes you the rest of the way — from an empty folder
to a **deployed, database-backed product that one person runs on one server**. That is the promise of
[the .NET One Person Framework](../one-person-framework.md), and this is the walk-through that proves it.

We build a small online shop — **Shop** — and grow it one chapter at a time. Every step is a real command
you type and real code the `rask` CLI writes for you. Nothing here is pseudo-code.

## What you'll build

By the last chapter, Shop has:

- a **Products** catalog with full create / read / update / delete pages, backed by SQLite through EF Core;
- an **Orders** feature;
- a **login** so only signed-in staff can edit the catalog;
- a **background job** that runs work off the request thread;
- **transactional email** (an order receipt) whose body is a Rask component;
- a **cache** in front of the catalog;
- **domain events** delivered through a durable outbox;
- **production-grade SQLite** — WAL, a busy-timeout, and continuous off-box backup;
- and finally, a **one-command deploy** to a single server behind automatic HTTPS.

Every one of those is a first-party Rask **pillar** — a thin, opinionated package that rides on the app's
own SQLite database. No Redis, no message broker, no managed queue, no second server. One box runs the
whole product.

## The chapters

| # | Chapter | Pillar | You'll run |
|---|---------|--------|-----------|
| 1 | [Scaffold the app](01-scaffold.md) | CLI · Auth | `rask new Shop --auth --docker` |
| 2 | [Your first feature](02-first-feature.md) | Data · CQRS · SQLite | `rask generate feature Product …` · `rask db` |
| 3 | [A second feature + locking it down](03-orders-and-auth.md) | Auth | `rask generate feature Order …` |
| 4 | [Background jobs](04-background-jobs.md) | Jobs | `rask generate job …` |
| 5 | [Transactional email](05-email.md) | Mail | `rask generate email …` |
| 6 | [Caching the catalog](06-cache.md) | Cache | `AddRaskCache<ProductsDbContext>()` |
| 7 | [Domain events + the outbox](07-outbox-events.md) | Outbox | `rask generate feature … --outbox` |
| 8 | [Production SQLite](08-production-sqlite.md) | SQLite | `UseRaskSqlite()` · Litestream |
| 9 | [Deploy to one box](09-deploy.md) | Deploy | `rask deploy --host … --domain …` |

Read them in order — each chapter builds on the app the previous one left behind. Every chapter ends with
a **Verify** section (how to confirm it works) and a **Learn more** link to that pillar's reference doc.

## Before you start

You need the **.NET 10 SDK** and the **`rask` CLI**:

```bash
dotnet --version                      # must be ≥ 10.0
dotnet tool install -g Rask.Cli       # installs the `rask` command (one-time)
```

If `dotnet --version` prints an older version, install the .NET 10 SDK from
[dotnet.microsoft.com](https://dotnet.microsoft.com/download) first. To upgrade an already-installed CLI
later, run `dotnet tool update -g Rask.Cli`.

This tutorial assumes you're comfortable with C#. It does **not** assume you know EF Core, CQRS, or Rask —
each idea is introduced where it first appears. If you've never built a Rask UI, skim
[Getting started](../getting-started.md) first; we won't re-teach components here.

Ready? → **[Chapter 1: Scaffold the app](01-scaffold.md)**
