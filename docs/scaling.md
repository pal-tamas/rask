# Scaling

How far one box goes, what happens when it restarts, and where the wall is.

Rask is built around one server running the whole product. That is a real position, not a limitation
we're apologising for — but it is only useful if you know what one server actually does, and what it
would take to need a second. Both numbers below come from reports in this repo, so you can rerun them
against your own app rather than trusting a table.

## What one box holds

Every live session pins a component tree, a DI scope, and a few buffers. Measure it:

```bash
dotnet run -c Release --project benchmarks/Rask.Benchmarks -- session-footprint
```

| Page | Connected | Sessions per GiB |
| --- | ---: | ---: |
| Empty shell | 16 KB | ~66,000 |
| 5-row table | 52 KB | ~20,300 |
| 200-row grid | 1.39 MB | ~735 |
| 1,000-row grid | 7.0 MB | ~146 |

**Page size, not user count, is what moves this** — a ~450× swing across the sweep. Sessions are cheap
until the page isn't. See [sizing `MaxSessions`](configuration.md#sizing-maxsessions-for-a-memory-budget)
for turning that into a cap, and note the two exclusions: Kestrel's ~32 KB per connection, and your own
scoped services — one `DbContext` per session can dwarf everything in the table.

**A page that needs nothing live can hold no session at all.** With
[`StaticPages`](render-modes.md) on, a page with no handler, form, `Ref` or JS call is served as a
plain document and its scope is released before the response is even written — so it never enters
this table. That also sharpens what `MaxSessions` means: it used to bound concurrent users *and*
`GET` traffic together, because every `GET` retained a session for ten seconds whether or not
anything ever connected to it. A crawler sweeping N routes created N sessions. Now it bounds
retained live sessions, which is what the name says.

Note the other side of that trade: waiting for a page's async data
([`InitialRenderQuiescenceTimeout`](render-modes.md)) holds an HTTP request open for up to that long,
so a slow page costs a request slot rather than a session. Size the two together.

## What one box serves

Fitting is not the same as serving. Measure that too:

```bash
dotnet run -c Release --project benchmarks/Rask.Benchmarks -- session-load
```

| Page | Events/sec | p50 | p99 |
| --- | ---: | ---: | ---: |
| Empty shell | ~100,000 | 0.15 ms | ~1 ms |
| 5-row table | ~85,000 | 0.19 ms | ~1 ms |
| 200-row grid | ~26,000 | 0.53 ms | ~6 ms |

Read the shape rather than the absolutes, because **the two tables have different shapes**. In memory, a
5-row page already costs 3× an empty shell. In throughput, they're within noise of each other, and the
cost arrives later but harder: a 200-row grid runs ~4× slower per event, because every interaction
re-renders and re-diffs the whole page.

The practical consequence: if you're worried about scale, **look at your largest page before you look at
your user count.** Splitting a 200-row grid into pages of 20 buys more than a bigger box will.

## What survives what

| Event | What the user sees |
| --- | --- |
| Brief network blip | Nothing. The socket reconnects against the intact session within `SessionGracePeriod` (30 s). |
| Tab backgrounded past the grace period | The session is gone, but the page is [rebuilt from the client's resume record](configuration.md#surviving-a-restart-or-a-redeploy) — same route, whatever state the app declared. |
| Deploy or restart | The departing host [hands clients over](deployment.md#the-shutdown-ladder): they reconnect immediately and rebuild, rather than sitting out a backoff and reloading. |
| Host at capacity | New sessions get `503` + `Retry-After`; `/health` reports Degraded at 80% of `MaxSessions` so an orchestrator sheds first. |
| Host killed outright | Whatever was in flight is lost. Sessions rebuild on the next reconnect if their record is still valid. |

Two of those depend on a **persisted data-protection key ring**, which `rask new` scaffolds to
`/data/keys`. Without it, a record sealed before a redeploy can't be opened after one, and you're back to
a reload. Watch `rask.sessions.resume_rejected{reason="unprotect"}` — a spike right after a deploy is
exactly that failure.

## The wall

**A Rask app on SQLite runs one writer.** [`sqlite.md`](sqlite.md#in-a-docker-container) already says
don't run `replicas > 1` against the same database, and that stands. Every DB-backed pillar — jobs, the outbox,
cache, mail, logging — writes to that database, so a second replica of a normal Rask app is not a
scaling step, it's a corruption risk.

That is the wall, and it is worth being precise about where it is:

- **It is not the session count.** A box holding tens of thousands of sessions of a modest page is
  ordinary.
- **It is not the event rate.** Tens of thousands of round trips per second is not where an app breaks.
- **It is the single writer**, and after that, whatever your own `DbContext` costs per session.

So `rask deploy` deliberately ships **no `--replicas` flag**. It would be a flag that is wrong for the
shape of app this framework tells you to build.

### Getting past it

In rough order of how much they buy for the effort:

1. **Shrink the page.** Paginate the grid, split the route. Cheapest, and it moves both tables at once.
2. **Get work off the request path.** [Jobs](jobs.md) and the [outbox](outbox.md) exist for this — a
   session waiting on a slow handler is a session holding its render lock.
3. **Get a bigger box.** Unfashionable and frequently correct. The measurements above are per-core-ish
   work; vertical headroom is real and requires changing nothing.
4. **Move the database.** Point EF Core at Postgres or SQL Server and the single-writer wall goes away.
   Rask has no opinion about your provider — but the [pillars are SQLite-shaped](one-person-framework.md),
   so this is where you start leaving the framework's happy path.
5. **Then, and only then, more than one host.** See below for what would need to change.

## If you run more than one host anyway

Some apps genuinely are read-mostly, or already on a client-server database. Nothing stops you putting
several Rask hosts behind a load balancer. What you need to know:

**Sessions no longer require sticky routing — but still prefer it.** A reconnect that lands on a host
which has never heard of the session used to mean "session timed out, reload". It now means the page is
rebuilt from the client's record. So affinity is an optimisation (an intact session is always better than
a rebuilt one), not a correctness requirement. For that to work across hosts, **every host must share a
data-protection key ring** — the record is sealed with it.

**Three things still require affinity, and will fail without it:**

| Surface | Why |
| --- | --- |
| File uploads | Staged to a node-local temp file; the WebSocket message that consumes them must reach the same host. |
| Downloads | `GET /_rask/download/{session}/{token}` reads a node-local entry. |
| Sign-in redeem | `POST /_rask/auth/redeem` reads an in-memory ticket issued on the host that authenticated you. |

Cookie-based affinity in the proxy covers all three. Rask does not configure one for you, and the
`rask deploy` Caddy setup routes to a single container per app.

**And the pillars assume one writer.** If jobs, the outbox or the cache are enabled and pointed at
SQLite, more than one host will fight over the same file. Move them to a shared database first, or don't
run them on more than one host. What the processors themselves do about several instances is below.

## Running more than one instance

The [jobs](jobs.md), [mail](mail.md) and [outbox](outbox.md) processors **lease** the work they claim, so a
job runs on one instance and an email is sent by one instance.

Claiming a batch is one `UPDATE` whose predicate re-tests claimability, re-evaluated against the row
version the winner committed, so the row goes to exactly one instance — no `SKIP LOCKED`, no
provider-specific SQL. A claim marks the rows with a token and an expiry (`LeaseDuration`, default 5
minutes); finishing hands them back, and so does a graceful shutdown, so a rolling deploy doesn't park a
batch.

This is what makes the *claim* safe when several processors race. It is a separate question from where the
database lives: on SQLite there is one writer to begin with, so the race the lease settles is one you only
reach by pointing the pillars at a shared client-server database yourself (see [Getting past
it](#getting-past-it) — that is outside the framework's happy path, and Rask ships no provider package for
it).

**A processor that dies keeps nothing.** Its lease simply runs out and the work becomes claimable again.
There is no sweeper to run and nothing to clean up by hand — expiry *is* the recovery mechanism, which is
why the claim tests the expiry rather than "is this row unclaimed".

### What a lease does not do

> Leases prevent one instance **overwriting another's outcome**. They do not make a side effect happen once.

If an instance overruns its `LeaseDuration`, a second instance may take the row and run the work again
while the first is still going. The first then finds its lease gone and discards its own result — the
database stays consistent — but if the work already sent an email, that email is out. At-least-once was
always the contract; the lease narrows the window from *always, on every instance* to *only when an
instance overruns its lease*.

So: **set `LeaseDuration` comfortably above your slowest handler**, and make handlers idempotent where the
side effect matters. When an overrun does happen you get a warning naming the option to raise:

```
Job 41 lost its lease mid-run on instance …; another instance owns it now.
Increase JobOptions.LeaseDuration past the time this job takes.
```

### `Attempts` counts attempts started

The claim increments `Attempts`, not the failure path. A job that takes the whole process down with it —
an OOM, a pod eviction — never reaches the failure path, so counting only failures would leave it retried
by every instance forever and `MaxAttempts` would never dead-letter it. A job that succeeds first time
shows `Attempts = 1`.

### The request path is a separate question

This is about the *background processors*. Serving traffic from several instances is its own problem:
[live sessions](architecture/live-rendering.md) hold an open WebSocket, a DI scope and a component tree in
one process, so a reconnect must reach the same instance — see [above](#if-you-run-more-than-one-host-anyway).

### Upgrading an existing app

The lease adds two nullable columns to the jobs, mail and outbox tables. Additive, no backfill — but the
migration is **not optional**:

```bash
rask db add AddLeases
rask db update
```

Skip it and the processors fail on every poll with `no such column: ClaimedUntil`. That failure is caught
and logged rather than crashing the app, so it looks healthy while doing nothing — which is why the log
says exactly these two commands rather than printing a stack trace.

## Where to look when it's slow

| Signal | Meaning |
| --- | --- |
| `rask.sessions.active` near `MaxSessions` | Capacity, not performance. Size the cap against your largest page. |
| `rask.handler.duration` p99 climbing | Your handlers, not the framework — the framework's own round trip is in the table above. |
| `rask.ws.frames.rejected{reason=backlog}` | A client is outrunning its own dispatch; the backpressure breaker is holding. |
| `rask.sessions.resume_rejected{reason=unprotect}` | The key ring isn't surviving deploys. |

Full list in [observability](observability.md).
