using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rask.Example.Shop.Features.Shared;

namespace Rask.Example.Shop.Features.Orders;

/// <summary>
/// Reacts to <see cref="OrderCreated"/> — the point where the pillars meet.
/// </summary>
/// <remarks>
/// <para>
/// This runs from the <b>outbox processor</b>, not inline with the request. <c>OrderCreated</c> was
/// written to the outbox table in the same transaction as the order itself, so either both committed or
/// neither did: there is no window in which an order exists but its confirmation was never scheduled.
/// The processor then relays it at-least-once, which is why the work below has to be safe to repeat.
/// </para>
/// <para>
/// Two more pillars hang off it. The confirmation goes to <b>Rask.Mail</b>, queued on the same database
/// and delivered off this thread, so a slow SMTP server can't fail the reaction. The follow-up goes to
/// <b>Rask.Jobs</b> as a delayed job, because "in a while, do this" is scheduled work rather than
/// something derived from the transaction — the distinction the two pillars exist to keep separate.
/// </para>
/// </remarks>
public sealed class OrderCreatedHandler(
    IDbContextFactory<AppDbContext> factory,
    IMailQueue mail,
    IJobQueue jobs,
    ILogger<OrderCreatedHandler> logger)
    : INotificationHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated notification, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var order = await db.Orders
            .FirstOrDefaultAsync(o => o.Id == notification.Id, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            // Deleted between the commit and the relay. At-least-once delivery makes this a normal race,
            // not an error — returning marks the message processed instead of retrying it forever.
            logger.LogInformation("Order {Id} no longer exists; nothing to confirm", notification.Id);
            return;
        }

        // The body is a Rask component — the same component model as the UI, rendered to HTML on send.
        await mail.SendAsync(
            Email.To("customer@example.com")
                .Subject($"Order confirmed: {order.Customer.Value}")
                .Body(OrderConfirmation(Customer: order.Customer.Value, Total: order.Total)),
            cancellationToken).ConfigureAwait(false);

        // Delayed rather than immediate: the cart cleanup only makes sense once the order has settled.
        await jobs.ScheduleAsync(new PurgeStaleCarts(), TimeSpan.FromSeconds(2), cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("Order {Id} confirmed for {Customer}", order.Id, order.Customer.Value);
    }
}
