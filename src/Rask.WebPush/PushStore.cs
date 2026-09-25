using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Data;
using Rask.Wire;

namespace Rask.WebPush;

/// <summary>The battery over a context: subscribers in a table, sends through the VAPID sender.</summary>
internal sealed class PushStore<TContext>(
    IDbContextFactory<TContext> contexts,
    IServiceProvider services,
    TimeProvider time,
    ILogger<PushStore<TContext>> logger) : IPush
    where TContext : DbContext
{
    public string? PublicKey => services.GetService<WebPushOptions>()?.VapidKeys?.PublicKey;

    public async Task<PushSubscriber> Subscribe(PushSubscription subscription, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription.Endpoint);

        var now = time.GetUtcNow().UtcDateTime;
        var userId = Current.UserId;

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var subscribers = db.Set<PushSubscriber>();

        // One row per endpoint: a browser that subscribes again — after a sign-in, with a renewed key pair —
        // replaces what it said last time rather than adding a second row a send would hit twice.
        var existing = await subscribers
            .FirstOrDefaultAsync(s => s.Endpoint == subscription.Endpoint, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = PushSubscriber.For(subscription, userId, now);
            subscribers.Add(existing);
        }
        else
        {
            existing.Renew(subscription, userId, now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return existing;
    }

    public async Task<bool> Unsubscribe(string endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var removed = await db.Set<PushSubscriber>()
            .Where(s => s.Endpoint == endpoint)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return removed > 0;
    }

    public Task<WebPushResult> Send(PushSubscription subscription, WebPushMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(message);

        return Sender().Send(subscription, message, cancellationToken);
    }

    public async Task<int> Deliver(WebPushMessage message, Guid? userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sender = Sender();

        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.Set<PushSubscriber>().AsQueryable();
        if (userId is { } user)
        {
            query = query.Where(s => s.UserId == user);
        }

        var subscribers = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        var delivered = 0;
        var gone = new List<Guid>();

        foreach (var subscriber in subscribers)
        {
            var result = await sender.Send(subscriber.Subscription, message, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                delivered++;
            }
            else if (result.ShouldDelete)
            {
                // 404/410: the browser unsubscribed or the push service dropped it. Kept, it would fail on every
                // send from here on, so the row goes with it.
                gone.Add(subscriber.Id);
            }
            else
            {
                logger.LogWarning(
                    "A push to {Endpoint} was not delivered ({Status}, HTTP {StatusCode}).",
                    subscriber.Endpoint, result.Status, result.StatusCode);
            }
        }

        if (gone.Count > 0)
        {
            await db.Set<PushSubscriber>()
                .Where(s => gone.Contains(s.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return delivered;
    }

    // Resolved per send, not in the constructor: the store has to exist — and the subscribe endpoints answer — on
    // an app that has no key pair yet, which a fresh clone of a scaffolded app is. Sending is what needs the keys,
    // and the sender checks them as it is built.
    private IWebPush Sender()
    {
        try
        {
            return services.GetRequiredService<IWebPush>();
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                "Web Push cannot send: " + ex.Message + " Set Rask:WebPush:VapidKeys:PublicKey and PrivateKey — "
                + "VapidKeys.Generate() mints a pair, and `rask new` writes one to appsettings.Development.json — "
                + "and Rask:WebPush:Subject, a mailto: or https: contact.",
                ex);
        }
    }
}
