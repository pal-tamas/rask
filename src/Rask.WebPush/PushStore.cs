using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Data;
using Rask.Wire;

namespace Rask.WebPush;

/// <summary>The battery over a context: subscribers in a table, sends through the VAPID sender.</summary>
internal sealed partial class PushStore<TContext>(
    IDbContextFactory<TContext> contexts,
    IServiceProvider services,
    TimeProvider time,
    ILogger<PushStore<TContext>> logger) : IPush
    where TContext : DbContext
{
    private const int SendsAtOnce = 8;

    public string? PublicKey => services.GetService<PushOptions>()?.VapidKeys?.PublicKey;

    public async Task<PushSubscriber> Subscribe(PushSubscription subscription, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (string.IsNullOrWhiteSpace(subscription.Endpoint))
        {
            throw new ArgumentException("A push subscription needs an endpoint.", nameof(subscription));
        }

        if (WebPushSender.Problem(subscription) is { } problem)
        {
            throw new ArgumentException(problem, nameof(subscription));
        }

        var now = time.GetUtcNow().UtcDateTime;
        var userId = Current.UserId;

        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var subscribers = db.Set<PushSubscriber>();

            // One row per endpoint: a browser that subscribes again — after a sign-in, with a renewed key pair —
            // replaces what it said last time rather than adding a second row a send would hit twice.
            var existing = await subscribers
                .FirstOrDefaultAsync(s => s.Endpoint == subscription.Endpoint, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                await RefuseAnonymousOverflow(subscribers, userId, cancellationToken).ConfigureAwait(false);
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
    }

    // A signed-out visitor's row costs whoever posts it nothing, so those are the ones that are counted. Counted
    // rather than tracked: the table is the truth, and two racing subscribes overshooting by one is not a leak.
    private async Task RefuseAnonymousOverflow(
        DbSet<PushSubscriber> subscribers, Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is not null || services.GetService<PushOptions>() is not { } options)
        {
            return;
        }

        var held = await subscribers.CountAsync(s => s.UserId == null, cancellationToken).ConfigureAwait(false);
        if (held >= options.MaxAnonymousSubscribers)
        {
            throw new PushSubscriberLimitException(options.MaxAnonymousSubscribers);
        }
    }

    public async Task<bool> Unsubscribe(string endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var removed = await db.Set<PushSubscriber>()
                .Where(s => s.Endpoint == endpoint)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            return removed > 0;
        }
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

        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var query = db.Set<PushSubscriber>().AsQueryable();
            if (userId is { } user)
            {
                query = query.Where(s => s.UserId == user);
            }

            var subscribers = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
            var delivered = 0;
            var gone = new ConcurrentBag<Guid>();

            // Several at once, so one endpoint that never answers costs a broadcast its own timeout rather
            // than everybody's: N dead rows used to hold a send for N timeouts in a row.
            await Parallel.ForEachAsync(
                subscribers,
                new ParallelOptions { MaxDegreeOfParallelism = SendsAtOnce, CancellationToken = cancellationToken },
                async (subscriber, stop) =>
                {
                    if (await TrySend(sender, subscriber, message, gone, stop).ConfigureAwait(false) is not { } result)
                    {
                        return;
                    }

                    if (result.IsSuccess)
                    {
                        Interlocked.Increment(ref delivered);
                    }
                    else if (result.ShouldDelete)
                    {
                        // 404/410: the browser unsubscribed or the push service dropped it. Kept, it would fail on
                        // every send from here on, so the row goes with it.
                        gone.Add(subscriber.Id);
                    }
                    else
                    {
                        NotDelivered(logger, subscriber.Endpoint, result.Status, result.StatusCode);
                    }
                }).ConfigureAwait(false);

            if (!gone.IsEmpty)
            {
                await db.Set<PushSubscriber>()
                    .Where(s => gone.Contains(s.Id))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return delivered;
        }
    }

    // One subscriber can never stop the rest: a row stored before subscriptions were validated can never be sent to,
    // so it goes; a push service that times out is skipped like any other transient failure. Null when skipped.
    private async Task<WebPushResult?> TrySend(
        IWebPush sender,
        PushSubscriber subscriber,
        WebPushMessage message,
        ConcurrentBag<Guid> gone,
        CancellationToken cancellationToken)
    {
        try
        {
            return await sender.Send(subscriber.Subscription, message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            // CryptographicException: a key of the right length that is not a point on the curve, which
            // only shows when it is used. Left to escape, that one row — anyone can post one — would
            // fail every broadcast, and never be removed.
            MalformedRemoved(logger, ex.Message);
            gone.Add(subscriber.Id);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TimedOut(logger, subscriber.Endpoint);
            return null;
        }
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
                "Web Push cannot send: " + ex.Message + " Set Rask:Push:VapidKeys:PublicKey and PrivateKey — "
                + "VapidKeys.Generate() mints a pair, and `rask new` writes one to appsettings.Development.json — "
                + "and Rask:Push:Subject, a mailto: or https: contact.",
                ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A push to {Endpoint} was not delivered ({Status}, HTTP {StatusCode}).")]
    private static partial void NotDelivered(ILogger logger, string endpoint, WebPushStatus status, int? statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A malformed push subscription was removed: {Problem}")]
    private static partial void MalformedRemoved(ILogger logger, string problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A push to {Endpoint} timed out.")]
    private static partial void TimedOut(ILogger logger, string endpoint);
}
