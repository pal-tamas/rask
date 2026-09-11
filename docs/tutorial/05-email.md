# Chapter 5 — Transactional email

> **Goal:** email the customer an order receipt — with the email body written as a Rask component.
> **You'll write:** an email component and wire it into the job from chapter 4.

`Rask.Mail` is the same story as jobs: durable rows in your `app.db`, a background sender that delivers them
over SMTP and retries on failure. The nice part is the body — it's a **Rask component**, so you write your
email in C# with the same `Div`/`H1` chain you already know, no templating language.

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
its chain sets), so add an `OrderId` and a `Total` and render them:

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

This is the one component in the tutorial written with plain tags rather than the Rask.Ui kit, and on
purpose: a mail client never loads the kit's stylesheet, so kit components would arrive unstyled.

## 2. It's already wired

Chapter 1's `rask new` already registered mail and mapped its table (`modelBuilder.AddRaskMail()` in
`AppDbContext`), and the first migration created it — so there is nothing to add and nothing to migrate. The
registration it wrote in `Program.cs` is:

```csharp
builder.Services.AddRaskMail<AppDbContext>(o =>
{
    o.From = "no-reply@example.com";
    o.PickupDirectory = builder.Configuration["Mail:PickupDirectory"] ?? "mail-pickup";
});
```

All that's left is your real sender address and, for production, an SMTP server. Edit that line:

```csharp
builder.Services.AddRaskMail<AppDbContext>(o =>
{
    o.From = "shop@example.com";
    o.PickupDirectory = builder.Configuration["Mail:PickupDirectory"] ?? "mail-pickup";

    // Prod: point at your SMTP server. Leave it unconfigured in development and every message is
    // written to ./mail-pickup as an .eml file instead of being sent.
    if (builder.Configuration["Mail:SmtpHost"] is { Length: > 0 } host)
    {
        o.Smtp = new SmtpOptions
        {
            Host = host,
            Port = 587,
            User = builder.Configuration["Mail:SmtpUser"],
            Password = builder.Configuration["Mail:SmtpPassword"],
        };
    }
});
```

> **Zero-config in development.** With no SMTP host configured, Rask.Mail doesn't try to reach a server — it
> writes each message to `mail-pickup`, so you can build and test the flow with no mail account. The
> credentials come from configuration: user-secrets on your machine, the deploy's environment file in
> production ([Chapter 11](11-deploy.md)) — never typed into `Program.cs`.

## 3. Send it from the job

Remember the `SendOrderReceipt` job from Chapter 4? That's exactly where the email belongs — off the request
thread. Inject `IMail` into the handler and send:

```csharp
using Shop.Features.Orders;   // for Order

public sealed class SendOrderReceiptHandler(IMail mail) : ICommandHandler<SendOrderReceipt>
{
    public async Task HandleAsync(SendOrderReceipt job, CancellationToken ct)
    {
        var order = await Order.FindAsync(job.OrderId, ct);
        if (order is null) return;

        // Hard-coded recipient for now — Order has no customer-email field yet; add one and use it here.
        await mail.SendAsync(
            Email.To("customer@example.com")
                 .Subject($"Your order {order.Id}")
                 .Body(OrderReceipt.OrderId(order.Id).Total(order.Total)),
            ct);
    }
}
```

`Order.FindAsync` returns `null` for an order that has been deleted since the job was queued, which is why
the handler checks before sending.

`Email.To(...)` is a fluent builder — chain `Subject(...)`, `Cc/Bcc`, `Attach(...)`, and `Body(component)`,
which renders your component to HTML right there. Note `Body(OrderReceipt.OrderId(…).Total(…))` builds the
component with its **chain**, not `new OrderReceipt(...)` — every Rask component is built that way (the
framework enforces it, [RASK014](../diagnostics.md#rask014)), and each public property is one step. `SendAsync`
just queues the row; the background sender delivers it. You now have the full chain: **place order → enqueue job
→ job sends email**, none of it on the customer's request.

## Verify

- With no SMTP host configured, placing an order writes a mail row and (within the poll interval) an `.eml`
  file in `mail-pickup` — body rendered from your `OrderReceipt` component.
- Configure `Mail:SmtpHost` for a real server (or a local catcher like Mailpit) and the receipt actually arrives.

**Learn more:** [transactional email](../mail.md) · [background jobs](../jobs.md)

Next → **[Chapter 6: Caching the catalog](06-cache.md)**
