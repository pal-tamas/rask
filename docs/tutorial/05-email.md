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
builder.Services.AddRaskMail<AppDbContext>();
```

All that's left is your real sender address and, for production, an SMTP server. Both are settings rather
than code — edit the `Rask:Mail` section `rask new` wrote into `appsettings.json`:

```jsonc
"Rask": {
  "Mail": {
    "From": "shop@example.com",
    // Dev: with no Smtp section, each message is written to this directory instead of sent.
    "PickupDirectory": "mail-pickup"
    // Prod: add "Smtp": { "Host": "smtp.example.com", "Port": 587, "User": "…" },
    // and put the password in the environment as Rask__Mail__Smtp__Password — never in this file.
  }
}
```

> **Zero-config in development.** With no `Smtp` section, Rask.Mail doesn't try to reach a server — it writes
> each message to `mail-pickup` as an `.eml` file, so you can build and test the flow with no mail account.
> The SMTP password comes from the environment — user-secrets on your machine, the deploy's environment file
> in production ([Chapter 11](11-deploy.md)) — never from `appsettings.json` or `Program.cs`.

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
- Add a `Smtp` section under `Rask:Mail` for a real server (or a local catcher like Mailpit) and the receipt actually arrives.

**Learn more:** [transactional email](../mail.md) · [background jobs](../jobs.md)

Next → **[Chapter 6: Caching the catalog](06-cache.md)**
