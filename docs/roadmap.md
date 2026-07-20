# Roadmap — the One Person Framework pillars

Rask's north star is the [.NET One Person Framework](one-person-framework.md): one developer builds, runs,
and ships a whole product from a single C# codebase on one server. This page tracks the pillars that serve
that goal — what's shipped and what's next. The through-line for everything stateful is **DB-backed by
default**: it rides the app's own SQLite database, so adding a capability is a package reference, not a new
service to operate.

## Shipped

| Pillar | Status | Where |
|--------|--------|-------|
| **UI across three hosts** | ✅ | Server (WebSocket live diff), WASM (client-side + PWA), Native iOS/Android *(preview)* — one component. |
| **The `rask` CLI** | ✅ | [`cli.md`](cli.md) — `new` / `dev` / `generate` / `db` / `deploy`. |
| **CRUD scaffolder** | ✅ | [`rask generate feature`](cli.md) — CQRS + EF Core vertical slice, value objects, validation, pages (tests with `--tests`); wires the DI into `Program.cs`. |
| **CQRS / mediator** | ✅ | [`Rask.Cqrs`](cqrs.md) — source-generated, reflection-free. |
| **Data layer** | ✅ | [`Rask.Data`](data.md) — `Entity<TId>` + interceptors (audit, soft delete, concurrency, domain events). |
| **Transactional outbox** | ✅ | [`Rask.Outbox`](outbox.md) — durable, crash-safe domain-event delivery on the app's own database. |
| **Background jobs** | ✅ | [`Rask.Jobs`](jobs.md) — durable enqueued/delayed/recurring work on the app's own database, at-least-once with backoff. |
| **Transactional email** | ✅ | [`Rask.Mail`](mail.md) — durable email queued on the app's own database, delivered off the request thread over SMTP; bodies are Rask components. |
| **Cache** | ✅ | [`Rask.Cache`](cache.md) — a developer-facing cache on the app's own database; standard `IDistributedCache` plus a typed `ICache` with `GetOrCreateAsync`, absolute/sliding expiry. |
| **Production SQLite** | ✅ | [`sqlite.md`](sqlite.md) — WAL/busy-timeout pragmas, continuous backup (Litestream), snapshots. |
| **Auth** | ✅ | [`authentication.md`](authentication.md) — cookie & JWT login/session in the templates. |
| **PWA & native** | ✅ | [`pwa.md`](pwa.md) / [`native.md`](native.md). |
| **Deploy to one box** | ✅ | [`rask deploy`](cli.md) — bare-VPS setup (Docker, deploy login, firewall, SSH hardening), build over SSH, zero-downtime, auto-HTTPS (Caddy), multi-app on one host, GitHub Actions. |

## Planned — DB-backed pillars

Each of these is designed to persist to the **same SQLite database the app already has** — no external
broker, no Redis, no separate infrastructure for a hello-world. Ordered by leverage for shipping a product.

### Cache — render/fragment cache
The developer-facing cache has [shipped](cache.md). Still planned: a render/fragment cache reusing the
framework's existing subtree-cache machinery, to memoize a component subtree across sessions by an explicit key.

### Broadcast
Server-to-many-clients pub/sub over the existing WebSocket channel — subscribe to a topic, push a live diff
to every subscriber. Unlocks realtime UI without new infrastructure.

---

Want to shape the direction? The framework is developed in the open — see
[the development workflow](development-workflow.md).
