# Roadmap — the One Person Framework pillars

Rask's north star is the [.NET One Person Framework](one-person-framework.md): one developer builds, runs,
and ships a whole product from a single C# codebase on one server. This page tracks the pillars that serve
that goal — what's shipped and what's next. The through-line for everything stateful is **DB-backed by
default**: it rides the app's own SQLite database, so adding a capability is a package reference, not a new
service to operate.

## Shipped

✅ shipped · ◐ partly, with documented limits · ❌ [not shipped](#not-shipped)

| Pillar | Status | Where |
|--------|--------|-------|
| **UI across three hosts** | ✅ | Server (WebSocket live diff), WASM (client-side + PWA), Native iOS/Android *(preview)* — one component. |
| **The `rask` CLI** | ✅ | [`cli.md`](cli.md) — `new` / `dev` / `db` / `deploy`. |
| **CRUD pattern** | ✅ | A CQRS + EF Core vertical slice — encapsulated entity, validation, pages — documented as code in [tutorial chapter 2](tutorial/02-first-feature.md), with jobs, email and cache following the same shape. |
| **CQRS / mediator** | ✅ | [`Rask.Cqrs`](cqrs.md) — source-generated, reflection-free. |
| **Data layer** | ✅ | [`Rask.Data`](data.md) — `Entity<TId>` + interceptors (audit, soft delete, concurrency, domain events). |
| **Transactional outbox** | ✅ | [`Rask.Outbox`](outbox.md) — durable, crash-safe domain-event delivery on the app's own database. |
| **Background jobs** | ✅ | [`Rask.Jobs`](jobs.md) — durable enqueued/delayed/recurring work on the app's own database, at-least-once with backoff. |
| **Transactional email** | ✅ | [`Rask.Mail`](mail.md) — durable email queued on the app's own database, delivered off the request thread over SMTP; bodies are Rask components. |
| **Cache** | ✅ | [`Rask.Cache`](cache.md) — a developer-facing cache on the app's own database; standard `IDistributedCache` plus a typed `ICache` with `GetOrCreateAsync`, absolute/sliding expiry. |
| **Production SQLite** | ✅ | [`sqlite.md`](sqlite.md) — WAL/busy-timeout pragmas, continuous backup (Litestream), snapshots. |
| **The door out of one box** | ❌ | Not shipped — Rask wires SQLite only. Jobs, mail and the outbox do **lease** the work they claim ([`scaling.md`](scaling.md#running-more-than-one-instance)), so the claim is safe when several processors race and a lease bounds, but does not eliminate, a duplicate side effect. See [below](#not-shipped). |
| **Auth — sign-in** | ✅ | [`authentication.md`](authentication.md) — cookie & JWT sessions, claims, authorization, and hardening guidance. |
| **Auth — user store** | ❌ | Not shipped. `rask new --auth` scaffolds a **demo** `ICredentialStore` with hardcoded logins, clearly marked as such; you supply the real one. See [below](#not-shipped). |
| **Web Push (server send)** | ✅ | [`webpush.md`](webpush.md) — `Rask.WebPush`: VAPID (RFC 8292) + aes128gcm (RFC 8291), zero deps. |
| **Deploy to one box** | ✅ | [`rask deploy`](cli.md) — bare-VPS setup (Docker, deploy login, firewall, SSH hardening), build over SSH, zero-downtime, auto-HTTPS (Caddy), multi-app on one host, GitHub Actions. |
| **Dead letters & queue health** | ✅ | [`dashboard.md`](dashboard.md) — `Rask.Dashboard` mounts `/_rask` over the outbox, jobs, mail and cache: queue depth, **what has given up**, the error behind it, and one click to retry. Plus the log (a live tail, and searchable history with [`Rask.Logging`](logging.md)) and the live SQLite pragmas. Fail-closed behind an authorization policy. |
| **Logs that survive a restart** | ✅ | [`logging.md`](logging.md) — `Rask.Logging` keeps the `ILogger` pipeline in a database of its own: buffered off the request thread, batched to disk, retention by age **and** row count, searchable from `/_rask`. |
| **Operate what you shipped** | ✅ | [`rask deploy status` / `logs` / `rollback`](cli.md) — what's running, its logs, and putting the previous image back. |
| **Continuous backup** | ✅ | [`sqlite.md`](sqlite.md) — `rask new --data` wires [Litestream](sqlite.md#continuous-backup-with-litestream); one variable at deploy time turns it on. |
| **Secrets** | ◐ | [`secrets.md`](secrets.md) — environment variables, remembered by name so a redeploy can't drop one. **No** vault, rotation, or encryption at rest. |

## Planned — DB-backed pillars

Each of these is designed to persist to the **same SQLite database the app already has** — no external
broker, no Redis, no separate infrastructure for a hello-world. Ordered by leverage for shipping a product.

### Cache — render/fragment cache
The developer-facing cache has [shipped](cache.md). Still planned: a render/fragment cache reusing the
framework's existing subtree-cache machinery, to memoize a component subtree across sessions by an explicit key.

### Broadcast
Server-to-many-clients pub/sub over the existing WebSocket channel — subscribe to a topic, push a live diff
to every subscriber. Unlocks realtime UI without new infrastructure.

## Not shipped

Listed because a roadmap that only says what exists isn't much use when you're deciding whether Rask fits.
None of these has an implementation today — if your product needs one, you'll be writing or renting it.

### A user store and account lifecycle
The [sign-in machinery](authentication.md) is real: cookie and JWT sessions, claims, authorization,
hardening guidance. What `rask new --auth` scaffolds behind it is a **demo** credential store with
hardcoded logins — honestly labelled in the generated code, but it is not a user system. There is no
registration, password hashing, reset flow, email verification, lockout, or MFA. Bring ASP.NET Core
Identity or a users table of your own.

### Another database
Rask wires **SQLite and nothing else**. There is no provider package for PostgreSQL or SQL Server, and
`rask new` has no database choice to make. Nothing stops you pointing EF Core at your own provider — the
[`Rask.Data`](data.md) aggregates, [`Rask.Cqrs`](cqrs.md) handlers and generated slices are
provider-agnostic — but you give up everything that treats the database as a file (Litestream, snapshots,
`rask db backup`, the deploy volume) and you are off the framework's happy path. See
[Scaling](scaling.md) for where the single-writer wall actually is.

### Offline-first sync and object storage
No CRDT replication, no op log, no `IObjectStore`. Several devices sharing one database with no server
between them is not something Rask does.

### File and blob storage
No `IBlobStorage`. Rask can move bytes between the browser and the server
([`http-and-files.md`](http-and-files.md)), but where an uploaded avatar is *kept* is your decision — the
local disk, S3, or a provider SDK you reference directly.

### Rate limiting
Nothing in the framework. The docs point you at a reverse-proxy rate limit in several places, and
`rask deploy` provisions a stock Caddy, which can't do it without a plugin — so today that means adding one
yourself, or a WAF in front. Worth knowing before you put a login form on the internet: there is **no
built-in login-attempt throttle**.

### Secrets beyond environment variables
See [`secrets.md`](secrets.md) for what does exist and, at the bottom, a blunt list of what doesn't.

### Horizontal scale
No `rask deploy --replicas`. Not an oversight, and not a "later" — it would be a flag that is wrong for
the shape of app this framework tells you to build. Every DB-backed pillar writes to the app's own SQLite
database, and [SQLite runs one writer](sqlite.md#in-a-docker-container), so a second replica of a normal
Rask app is a corruption risk rather than a scaling step.

What one box actually does is [measured, not asserted](scaling.md) — sessions held and events served,
both reproducible from reports in this repo. Read that before assuming you need a second box; the usual
answer is a smaller page. If you genuinely do outgrow it, the door is a client-server database, and
`scaling.md` covers what else would have to change (uploads, downloads and sign-in redeem still require
sticky routing; sessions no longer do).

---

Every pillar above is wired together in [`samples/Rask.Example.Shop`](../samples/Rask.Example.Shop), and
built up one chapter at a time in the [tutorial](tutorial/00-overview.md).

Want to shape the direction? The framework is developed in the open — see
[the development workflow](development-workflow.md).
