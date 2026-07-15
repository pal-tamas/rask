using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Cqrs;

namespace Rask.Outbox;

/// <summary>
/// Polls the <see cref="OutboxMessage"/> table on a schedule and publishes each unprocessed message through
/// <c>Rask.Cqrs</c>' <see cref="IDispatcher"/>, marking it processed (or recording the failure + attempt
/// count). At-least-once: a message publishes at least once and is retried up to
/// <see cref="OutboxOptions.MaxAttempts"/>. A publish failure never crashes the app.
/// </summary>
/// <typeparam name="TContext">The application's <see cref="DbContext"/> that owns the outbox table.</typeparam>
public sealed class OutboxProcessor<TContext>(
    IDbContextFactory<TContext> contextFactory,
    IServiceScopeFactory scopeFactory,
    OutboxOptions options,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PollInterval);
        try
        {
            do
            {
                await DrainAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var batch = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null && m.Attempts < options.MaxAttempts)
            .OrderBy(m => m.Id)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (batch.Count == 0)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        foreach (var message in batch)
        {
            var notification = OutboxSerializerRegistry.Deserialize(message.Type, message.Payload);
            if (notification is null)
            {
                message.Attempts++;
                message.Error = $"No registered outbox event type '{message.Type}'.";
                logger.LogError("Outbox message {Id} has an unregistered type '{Type}'.", message.Id, message.Type);
                continue;
            }

            try
            {
                await dispatcher.PublishAsync(notification, cancellationToken).ConfigureAwait(false);
                message.ProcessedAt = timeProvider.GetUtcNow().UtcDateTime;
                message.Error = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break; // shutting down — leave the rest for the next run
            }
#pragma warning disable CA1031 // A failing handler must not stop the drain or crash the app — record + retry.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                message.Attempts++;
                message.Error = ex.Message;
                logger.LogError(ex, "Outbox message {Id} failed to publish (attempt {Attempts}).", message.Id, message.Attempts);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
