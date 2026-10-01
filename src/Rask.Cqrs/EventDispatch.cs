using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

/// <summary>
/// Fan-out helper the source-generated event invokers call. Public only so generated code can
/// reach it; you do not use it directly.
/// </summary>
public static class EventDispatch
{
    /// <summary>Runs every handler for an event using the configured <see cref="EventPublishStrategy"/>.</summary>
    public static async Task PublishAll<TEvent>(
        IServiceProvider provider,
        TEvent e,
        IEnumerable<IEventHandler<TEvent>> handlers,
        CancellationToken cancellationToken)
        where TEvent : IEvent
    {
        var options = provider.GetService<CqrsExecutionOptions>() ?? CqrsExecutionOptions.Default;

        if (options.PublishStrategy == EventPublishStrategy.WhenAll)
        {
            await Task.WhenAll(handlers.Select(h => h.Handle(e))).ConfigureAwait(false);
            return;
        }

        List<Exception>? errors = null;
        foreach (var handler in handlers)
        {
            try
            {
                await handler.Handle(e).ConfigureAwait(false);
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
