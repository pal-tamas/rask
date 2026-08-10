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
run them on more than one host.

## Where to look when it's slow

| Signal | Meaning |
| --- | --- |
| `rask.sessions.active` near `MaxSessions` | Capacity, not performance. Size the cap against your largest page. |
| `rask.handler.duration` p99 climbing | Your handlers, not the framework — the framework's own round trip is in the table above. |
| `rask.ws.frames.rejected{reason=backlog}` | A client is outrunning its own dispatch; the backpressure breaker is holding. |
| `rask.sessions.resume_rejected{reason=unprotect}` | The key ring isn't surviving deploys. |

Full list in [observability](observability.md).
