# Chapter 5 — Transactional email

> **Goal:** email the customer an order receipt — with the email body written as a Rask component.
> **You'll run:** `rask generate email OrderReceipt`

`Rask.Mail` is the same story as jobs: durable rows in your `app.db`, a background sender that delivers them
over SMTP and retries on failure. The nice part is the body — it's a **Rask component**, so you write your
email in C# with the same `Div()`/`H1()` you already know, no templating language.

## 1. Generate an email

```bash
rask generate email OrderReceipt
```

That writes `Emails/OrderReceipt.cs` — a component whose `Render()` is the email body:

```csharp
public sealed class OrderReceipt : Component
{
    protected override Component? Render() =>
    [
        Div()["OrderReceipt works. Edit Render() to build the email body."]
    ];
}
```

Give it the order data and build a real body:

```csharp
public sealed class OrderReceipt(Guid orderId, decimal total) : Component
{
    protected override Component? Render() =>
        Div()[
            H1()["Thanks for your order!"],
            P()[$"Order {orderId} — total ", Strong()[$"{total:C}"], "."],
            P()["We'll email again when it ships."]
        ];
}
```

## 2. Wire it up

One registration line and the mail table. In `Program.cs`:

```csharp
builder.Services.AddRaskMail<ProductsDbContext>(o =>
{
    o.From = "shop@example.com";
    // Dev: leave Smtp unset and mail is written to a pickup directory / logged instead of sent.
    // Prod: point at your SMTP server.
    o.Smtp = new SmtpOptions { Host = "smtp.example.com", Port = 587, User = "…", Password = "…" };
});
```

Map the table in `ProductsDbContext.OnModelCreating`:

```csharp
modelBuilder.AddRaskMail();        // ← the QueuedMail table
```

Migrate:

```bash
rask db add AddMail
rask db update
```

> **Zero-config in development.** If you omit `o.Smtp`, Rask.Mail doesn't try to reach a server — it writes
> messages to a pickup directory (or logs them), so you can build and test the flow with no mail account.

## 3. Send it from the job

Remember the `SendOrderReceipt` job from Chapter 4? That's exactly where the email belongs — off the request
thread. Inject `IMailQueue` into the handler and send:

```csharp
public sealed class SendOrderReceiptHandler(
    IDbContextFactory<ProductsDbContext> dbFactory,
    IMailQueue mail) : ICommandHandler<SendOrderReceipt>
{
    public async Task HandleAsync(SendOrderReceipt job, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.Orders.FindAsync([job.OrderId], ct);
        if (order is null) return;

        await mail.SendAsync(
            Email.To("customer@example.com")
                 .Subject($"Your order {order.Id}")
                 .Body(new OrderReceipt(order.Id, order.Total)),
            ct);
    }
}
```

`Email.To(...)` is a fluent builder — chain `Subject(...)`, `Cc/Bcc`, `Attach(...)`, and `Body(component)`,
which renders your component to HTML right there. `SendAsync` just queues the row; the background sender
delivers it. You now have the full chain: **place order → enqueue job → job sends email**, none of it on the
customer's request.

## Verify

- With `Smtp` unset, placing an order writes a mail row and (within the poll interval) a message file to the
  pickup directory / log — body rendered from your `OrderReceipt` component.
- Point `Smtp` at a real server (or a local catcher like Mailpit) and the receipt actually arrives.

**Learn more:** [transactional email](../mail.md) · [background jobs](../jobs.md)

Next → **[Chapter 6: Caching the catalog](06-cache.md)**
