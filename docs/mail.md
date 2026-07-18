# Rask.Mail — durable transactional email on your database

`Rask.Mail` sends **transactional email off the request thread**, queued in the app's own database — no message
broker, no Redis. Compose an email whose body is a **Rask component rendered to HTML**, call
`SendAsync`, and a hosted worker delivers it later over SMTP, **at-least-once**, with exponential-backoff
retries. It also sends **delayed** email and works with **zero configuration** in development.

```bash
dotnet add package Rask.Mail
```

## Why queue email

Sending mail inline with a request couples the response to a slow, flaky third party: the SMTP server is down,
or takes two seconds, and your user waits (or sees an error) for something that isn't really part of their
action. You want to return immediately and deliver the mail in the background, **durably**: if the process
restarts the message isn't lost, and a transient SMTP failure is retried rather than dropped.

`Rask.Mail` persists each email to a table in your database and a hosted worker polls it, so there's nothing
else to run. And because an email body is just a **component**, you compose it with the same render pipeline
you already use for pages — no separate templating language.

## Use

```csharp
public sealed class WelcomeEmail(string name) : Component
{
    protected override Component? Render() =>
        Div()[H1()[$"Welcome, {name}!"], P()["Thanks for signing up."]];
}

// Program.cs
builder.Services.AddRaskMail<AppDbContext>(o =>
{
    o.From = "hello@example.com";
    o.Smtp = new SmtpOptions { Host = "smtp.example.com", Port = 587, User = "…", Password = "…" };
    o.MaxAttempts = 10;
});

builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    modelBuilder.AddRaskMail();   // maps the QueuedMail table
}
```

Add a migration for the new table before running — `rask db add AddMail && rask db update`
(or `dotnet ef migrations add AddMail` directly). Then send from anywhere `IMailQueue` is injected:

```csharp
await mail.SendAsync(Email
    .To(user.Email, user.Name)
    .Subject("Welcome")
    .Body(new WelcomeEmail(user.Name)));                       // send asap

await mail.ScheduleAsync(reminder, delay: TimeSpan.FromHours(24));  // send later
```

### Zero-config in development

You don't need an SMTP server to develop. If `Smtp` is not set, `Rask.Mail` falls back to:

- **a pickup directory** — set `o.PickupDirectory = "sent-mail"` and each message is written as an `.eml` file
  you can open in any mail client; or
- **logging** — with neither `Smtp` nor `PickupDirectory` set, each send is logged (`"would send email to …"`).

Switch to real delivery in production by setting `o.Smtp`. Nothing else changes.

## How it works

- **`Email`** — a fluent builder: `To`/`AndTo`/`Cc`/`Bcc`/`ReplyTo`/`From`, `Subject`, `Body(component)` (or
  `Body(html)`), an optional `PlainText` alternative, and `Attach`. `Body(component)` renders the component to
  HTML **immediately** (`Component.ToHtml()`), so the built email holds only strings and bytes.
- **`IMailQueue`** — writes one `QueuedMail` row (envelope + already-rendered body) through your
  `IDbContextFactory<TContext>`, defaulting the sender from `MailOptions.From` when the message didn't set one.
  Because the body is rendered at enqueue time, the stored row is self-contained — there's no component to
  reconstruct when it's sent.
- **`MailProcessor<TContext>`** — a hosted `BackgroundService` that polls on `PollInterval` for **due** messages
  (`RunAt <= now`, oldest first), hands each to the registered `IMailSender`, and stamps `ProcessedAt`. On
  failure it records the error, increments the attempt count, and pushes `RunAt` out by an **exponential
  backoff** (`BaseRetryDelay × 2^(attempts-1)`, capped at `MaxRetryDelay`), retrying until `MaxAttempts` — after
  which the message is left as a **dead letter** for inspection. A failing send never crashes the app. Sent
  messages are purged after `RetentionPeriod` (default 7 days; `TimeSpan.Zero` keeps them).
- **`IMailSender`** — the delivery seam. `AddRaskMail` picks `MailKitMailSender` (SMTP) when `Smtp` is set,
  else `PickupDirectoryMailSender`, else `LogMailSender`. Register your own `IMailSender` **before**
  `AddRaskMail` to send through a provider API instead.

## Notes

- **Server-side.** The processor is a hosted service and the store is your EF Core database — this is not a
  browser/WASM concern.
- **SQLite is single-writer**, so the processor polls and claims sequentially. Run **one processor per app**.
  Because `SendAsync` writes while the processor may also be writing, use [`UseRaskSqlite`](sqlite.md) (WAL + a
  `busy_timeout`) on your context so a concurrent send waits for the write lock instead of failing with
  `SQLITE_BUSY`.
- **Mail vs. jobs.** `Rask.Mail` is a self-contained queue — you don't need [`Rask.Jobs`](jobs.md). If you
  already run jobs and want email as one step of a larger job, send it inline from the job's handler via a
  custom `IMailSender`; otherwise `SendAsync` is all you need.
