# Chapter 5 — Transactional email

> **Goal:** email the customer an order receipt — with the email body written as a Rask component.
> **You'll write:** an email component and wire it into the job from chapter 4.

`Rask.Mail` is the same story as jobs: durable rows in your `app.db`, a background sender that delivers them
over SMTP and retries on failure. The nice part is the body — it's a **Rask component**, so you write your
email in C# with the same `Div()`/`H1()` you already know, no templating language.

## 1. Write an email

Create `Features/Shared/OrderReceipt.cs` — a component whose `Render()` is the email body:

```csharp
namespace Shop.Features.Shared;

public sealed partial class OrderReceipt : Component
{
    protected override Component? Render() =>
    [
        Div["OrderReceipt works. Edit Render() to build the email body."]
    ];
}
```

Give it the order data and build a real body. A component carries data on **public properties** (that's what
the generated factory fills in), so add an `OrderId` and a `Total` and render them:

```csharp
public sealed partial class OrderReceipt : Component
{
    public Guid OrderId { get; set; }
    public decimal Total { get; set; }

    protected override Component? Render() =>
        Div[
            H1["Thanks for your order!"],
            P[$"Order {OrderId} — total ", Strong[$"{Total:C}"], "."],
            P["We'll email again when it ships."]
        ];
}
```

## 2. It's already wired

`--all-batteries` already registered mail in Chapter 1, so there is nothing to add. Had you scaffolded
without it, these are the two lines to add yourself:

- `builder.Services.AddRaskMail<AppDbContext>(…)` in `Program.cs`, and
- the mail table mapped with `modelBuilder.AddRaskMail();` in `OnModelCreating`.

All that's left is your real sender address and, for production, an SMTP server — edit the registration it added:

```csharp
builder.Services.AddRaskMail<AppDbContext>(o =>
{
    o.From = "shop@example.com";
    // Dev: leave Smtp unset and mail is written to a pickup directory / logged instead of sent.
    // Prod: point at your SMTP server.
    o.Smtp = new SmtpOptions { Host = "smtp.example.com", Port = 587, User = "…", Password = "…" };
});
```

Then create the table:

```bash
rask db add AddMail
rask db update
```

> **Zero-config in development.** If you omit `o.Smtp`, Rask.Mail doesn't try to reach a server — it writes
> messages to a pickup directory (or logs them), so you can build and test the flow with no mail account.
>
> **No database yet?** `AddRaskMail<TContext>` needs a `DbContext` to queue into — add the two lines above
> once you have one (chapter 2 writes it).

## 3. Send it from the job

Remember the `SendOrderReceipt` job from Chapter 4? That's exactly where the email belongs — off the request
thread. Inject `IMailQueue` into the handler and send:

```csharp
public sealed class SendOrderReceiptHandler(
    IDbContextFactory<AppDbContext> dbFactory,
    IMailQueue mail) : ICommandHandler<SendOrderReceipt>
{
    public async Task HandleAsync(SendOrderReceipt job, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.Orders.FindAsync([job.OrderId], ct);
        if (order is null) return;

        // Hard-coded recipient for now — Order has no customer-email field yet; add one and use it here.
        await mail.SendAsync(
            Email.To("customer@example.com")
                 .Subject($"Your order {order.Id}")
                 .Body(OrderReceipt(OrderId: order.Id, Total: order.Total)),
            ct);
    }
}
```

`Email.To(...)` is a fluent builder — chain `Subject(...)`, `Cc/Bcc`, `Attach(...)`, and `Body(component)`,
which renders your component to HTML right there. Note `Body(OrderReceipt(OrderId: …, Total: …))` calls the
**generated `OrderReceipt` factory**, not `new OrderReceipt(...)` — every Rask component is built through its
factory (the framework enforces this), and the factory takes one named argument per public property. `SendAsync`
just queues the row; the background sender delivers it. You now have the full chain: **place order → enqueue job
→ job sends email**, none of it on the customer's request.

## Verify

- With `Smtp` unset, placing an order writes a mail row and (within the poll interval) a message file to the
  pickup directory / log — body rendered from your `OrderReceipt` component.
- Point `Smtp` at a real server (or a local catcher like Mailpit) and the receipt actually arrives.

**Learn more:** [transactional email](../mail.md) · [background jobs](../jobs.md)

Next → **[Chapter 6: Caching the catalog](06-cache.md)**
