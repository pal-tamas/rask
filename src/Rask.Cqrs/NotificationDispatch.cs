using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

/// <summary>The runtime snapshot of the notification-fan-out knobs from <see cref="CqrsOptions"/>.</summary>
internal sealed class CqrsExecutionOptions
{
    internal static readonly CqrsExecutionOptions Default = new();

    public NotificationPublishStrategy PublishStrategy { get; init; } = NotificationPublishStrategy.Sequential;

    public bool StopOnFirstException { get; init; } = true;
}

/// <summary>
/// Fan-out helper the source-generated notification invokers call. Public only so generated code can
/// reach it; you do not use it directly.
/// </summary>
public static class NotificationDispatch
{
    /// <summary>Runs every handler for a notification using the configured <see cref="NotificationPublishStrategy"/>.</summary>
    public static async Task PublishAll<TNotification>(
        IServiceProvider provider,
        TNotification notification,
        IEnumerable<INotificationHandler<TNotification>> handlers,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        var options = provider.GetService<CqrsExecutionOptions>() ?? CqrsExecutionOptions.Default;

        if (options.PublishStrategy == NotificationPublishStrategy.WhenAll)
        {
            await Task.WhenAll(handlers.Select(h => h.Handle(notification, cancellationToken))).ConfigureAwait(false);
            return;
        }

        List<Exception>? errors = null;
        foreach (var handler in handlers)
        {
            try
            {
                await handler.Handle(notification, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (options.StopOnFirstException)
                {
                    throw;
                }

                (errors ??= []).Add(ex);
            }
        }

        if (errors is { Count: > 0 })
        {
            throw new AggregateException(errors);
        }
    }
}
