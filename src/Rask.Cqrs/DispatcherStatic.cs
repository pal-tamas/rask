using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

/// <summary>
///     Queries, commands and notifications, with nothing injected — from a handler, a render, a request, a job:
/// </summary>
/// <remarks>
///     <code>
///     var products = await Dispatcher.Query(new GetProducts());
///     await Dispatcher.Send(new PlaceOrder(cart.Id));
///     await Dispatcher.Publish(new OrderPlaced(order.Id));
///     </code>
///     <para>
///         Each call reaches the <see cref="IDispatcher" /> of the work it runs in, and the handler it reaches is
///         cancelled with that work. Outside any — a hosted service, a timer started at boot — it throws; inject
///         <see cref="IDispatcher" /> there instead.
///     </para>
/// </remarks>
public static class Dispatcher
{
    /// <summary>Runs <paramref name="query" /> through its handler and hands back what it answers.</summary>
    public static Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
        Resolve().Query(query, cancellationToken);

    /// <summary>Sends <paramref name="command" /> to its handler.</summary>
    public static Task Send(ICommand command, CancellationToken cancellationToken = default) =>
        Resolve().Send(command, cancellationToken);

    /// <summary>Sends <paramref name="command" /> to its handler and hands back what it answers.</summary>
    public static Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default) =>
        Resolve().Send(command, cancellationToken);

    /// <summary>Publishes <paramref name="notification" /> to every handler of it.</summary>
    public static Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification =>
        Resolve().Publish(notification, cancellationToken);

    private static IDispatcher Resolve()
    {
        var services = Ambient.Services
            ?? throw new InvalidOperationException(
                "Dispatcher was called outside any work in progress — a handler, a render, a request or a job — so "
                + "there is no app to reach. Inject IDispatcher in the constructor there instead.");

        return services.GetService<IDispatcher>()
            ?? throw new InvalidOperationException("Dispatcher needs Rask.Cqrs registered: call builder.Services.AddRaskCqrs().");
    }
}
