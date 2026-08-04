# Chapter 10 — Watching it run

> **Goal:** be able to answer "is the background work actually happening?" without SSH-ing into anything.
> **You'll add:** an `/ops` page that reads every pillar's own table.

Shop now has five background workers: the outbox relay, the job processor, the mail sender, the cache purger
and the snapshot service. All of them succeed silently and fail silently. Before you deploy, you want one
page that says what they're doing.

Here is where the DB-backed design pays off a second time. There is no broker console to open and no
separate metrics stack to stand up, because **every pillar's state is a table in the database you already
query**. Queue depth is `SELECT count(*)`.

## 1. The tables

| Pillar | Table | The question it answers |
|--------|-------|------------------------|
| `Rask.Outbox` | `OutboxMessage` | How many events are waiting, delivered, or erroring? |
| `Rask.Jobs` | `Job`, `RecurringJobState` | Is anything queued, and did the recurring work fire? |
| `Rask.Mail` | `QueuedMail` | Is mail going out, or piling up? |
| `Rask.Cache` | `CacheEntry` | How much is cached right now? |

They're ordinary EF entities. `db.Set<OutboxMessage>()` works exactly like `db.Products`.

## 2. Count what matters

Three counts tell you almost everything:

```csharp
OutboxProcessed = await db.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt != null, ct),
OutboxFailed    = await db.Set<OutboxMessage>().CountAsync(m => m.Error != null, ct),
JobsProcessed   = await db.Set<Job>().CountAsync(j => j.ProcessedAt != null, ct),
```

The **failed** count is the one people leave out, and it's the one that matters. Delivery is at-least-once
with retries: a message the processor can't handle doesn't throw and doesn't disappear — it records an
`Error`, increments `Attempts`, and comes back on the next poll until it hits `MaxAttempts`. A dashboard
that only shows "processed" reports a healthy system while an event retries itself to death.

So: processed climbing is good, **failed above zero is the alert**.

## 3. Make it live

A page that needs refreshing won't get looked at. Poll in the background and re-render on a real change:

```csharp
protected override async Task OnMountAsync()
{
    await RefreshAsync().ConfigureAwait(false);
    _ = PollAsync();
}

private async Task PollAsync()
{
    for (var tick = 0; tick < MaxTicks && !_stopped.IsCancellationRequested; tick++)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), _stopped.Token).ConfigureAwait(false);

        var before = _stats;
        await RefreshAsync().ConfigureAwait(false);
        if (_stats != before)
        {
            StateHasChanged();
        }
    }
}
```

Three things in that loop are deliberate:

- **`ConfigureAwait(false)` on every await.** It keeps the loop off the lifecycle sync-context, so the
  framework doesn't render once per `await`. You call `StateHasChanged()` yourself, once per real change.
- **Compare before re-rendering.** `_stats` is a `readonly record struct`, so `!=` is a value comparison. An
  idle system produces no diffs and no WebSocket traffic.
- **The loop is bounded.** Every open tab is a reader competing with five writers for SQLite's single write
  lock. An unbounded 1 Hz poll per session is a real cost — stop after a few minutes.

## 4. Prove the pragmas

While you're here, read the connection settings back from the live database, rather than trusting that
`UseRaskSqlite` did what Chapter 8 said:

```csharp
JournalMode = await ScalarAsync(db, "PRAGMA journal_mode"),   // → wal
ForeignKeys = await ScalarAsync(db, "PRAGMA foreign_keys"),   // → 1
```

## Verify

- Place an order and watch, without touching anything: outbox processed goes up, then mail sent, then jobs
  processed — the chain from Chapter 7 moving through three pillars.
- Outbox failed stays at `0`.
- `journal_mode` reads `wal` and `foreign_keys` reads `1`.
- **See it running:** [`samples/Rask.Example.Shop`](../../samples/Rask.Example.Shop)'s `/ops` is exactly this
  page, and its browser tests assert each of those counters moves.

> **Beyond counters.** Rask also exposes a live-session health check
> (`AddHealthChecks().AddRaskLiveSessions()`) that `rask deploy` probes to gate a zero-downtime swap, and
> standard `ILogger` output goes wherever you point it. See [observability](../observability.md).

**Learn more:** [observability](../observability.md) · [jobs](../jobs.md) · [outbox](../outbox.md)

Next → **[Chapter 11: Deploy to one box](11-deploy.md)**
