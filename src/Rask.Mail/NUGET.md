# Rask.Mail

**Durable transactional email** for a Rask app — queued on the app's own database and delivered off the
request thread, with no broker or Redis.

- Compose with a fluent **`Email`** builder; the body is a **Rask component rendered to HTML**, so an email
  template is just another component.
- Call **`IMailQueue.SendAsync(email)`** and it persists one `QueuedMail` row; a background **`MailProcessor`**
  delivers it over SMTP — **at-least-once**, with **exponential-backoff** retries up to `MaxAttempts` (then
  left as a dead letter for inspection).
- **Delayed** send with `ScheduleAsync(email, delay)`.
- **Zero-config in development** — with no SMTP configured, mail is logged; point `PickupDirectory` at a folder
  to write `.eml` files instead. Production sends over SMTP via [MailKit](https://github.com/jstedfast/MailKit).

## Use

```csharp
public sealed class WelcomeEmail : Component
{
    public string Name { get; set; } = "";

    protected override Component? Render() =>
        Div()[H1()[$"Welcome, {Name}!"], P()["Thanks for signing up."]];
}

// Program.cs
builder.Services.AddRaskMail<AppDbContext>(o =>
{
    o.From = "hello@example.com";
    o.Smtp = new SmtpOptions { Host = "smtp.example.com", Port = 587, User = "…", Password = "…" };
});

builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));

// AppDbContext.OnModelCreating:  modelBuilder.AddRaskMail();
// then:  rask db add AddMail && rask db update
```

```csharp
// send from anywhere IMailQueue is injected:
await mail.SendAsync(Email
    .To(user.Email, user.Name)
    .Subject("Welcome")
    .Body(WelcomeEmail(Name: user.Name)));   // the generated factory, not new (RASK014)

await mail.ScheduleAsync(reminder, delay: TimeSpan.FromHours(24));
```

Register your context as an `IDbContextFactory<AppDbContext>`. Several instances is safe: each processor
**leases** the work it claims. On SQLite you will still usually run one, because SQLite is
single-writer, so the processor claims work by polling and writing. Use
[`UseRaskSqlite`](https://www.nuget.org/packages/Rask.SQLite) (WAL + a `busy_timeout`) so a concurrent send
waits for the write lock instead of failing.

Part of [Rask](https://www.nuget.org/packages/Rask.Server) — the .NET One Person Framework.
