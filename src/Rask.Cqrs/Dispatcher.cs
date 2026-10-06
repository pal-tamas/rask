using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

/// <summary>
///     Queries, commands and events, with nothing injected — from anywhere:
/// </summary>
/// <remarks>
///     <code>
///     var products = await Dispatcher.Query(new GetProducts());
///     await Dispatcher.Send(new PlaceOrder(cart.Id));
///     await Dispatcher.Publish(new OrderPlaced(order.Id));
///     </code>
///     <para>
///         Inside work in progress — a handler, a render, a request, a job — each call reaches the
///         <see cref="IDispatcher" /> of that work, and the handler it reaches is cancelled with it.
///     </para>
///     <para>
///         Outside any — a <c>BackgroundService</c>, a timer, a webhook — the call opens a scope of its own and
///         disposes it once the handler has finished, so a singleton publishes with the same line a page does.
///     </para>
/// </remarks>
public static class Dispatcher
{
    private static IServiceProvider? _root;

    /// <summary>Whether a call has an app to reach: work is in progress, or the app has started.</summary>
    public static bool IsOn => _root is not null || Ambient.Services?.GetService<IDispatcher>() is not null;

    /// <summary>Runs <paramref name="query" /> through its handler and hands back what it answers.</summary>
    public static Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
        Ambient.Services is { } work
            ? Of(work).Query(query, cancellationToken)
            : Alone(query, static (d, q, ct) => d.Query(q, ct), cancellationToken);

    /// <summary>Sends <paramref name="command" /> to its handler.</summary>
    public static Task Send(ICommand command, CancellationToken cancellationToken = default) =>
        Ambient.Services is { } work
            ? Of(work).Send(command, cancellationToken)
            : Alone(command, static (d, c, ct) => d.Send(c, ct), cancellationToken);

    /// <summary>Sends <paramref name="command" /> to its handler and hands back what it answers.</summary>
    public static Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default) =>
        Ambient.Services is { } work
            ? Of(work).Send(command, cancellationToken)
            : Alone(command, static (d, c, ct) => d.Send(c, ct), cancellationToken);

    /// <summary>Publishes <paramref name="e" />: every subscription watching it is told, and every handler of it runs.</summary>
    public static Task Publish<TEvent>(TEvent e, CancellationToken cancellationToken = default)
        where TEvent : IEvent =>
        Ambient.Services is { } work
            ? Of(work).Publish(e, cancellationToken)
            : Alone(e, static (d, published, ct) => d.Publish(published, ct), cancellationToken);

    /// <summary>Remembers the app's root provider, which a call outside any work opens its scope from.</summary>
    internal static void Start(IServiceProvider root) => _root = root;

    private static IDispatcher Of(IServiceProvider services) =>
        services.GetService<IDispatcher>()
        ?? throw new InvalidOperationException(
            "Dispatcher is not running in this app. A RaskApp has it on unless Program.cs says c.Cqrs.Off(); a "
            + "hand-wired host calls builder.Services.AddRaskCqrs().");

    // Awaits the handler before the scope goes: disposing it underneath would take away the services it was given.
    private static async Task Alone<TMessage>(
        TMessage message, Func<IDispatcher, TMessage, CancellationToken, Task> call, CancellationToken cancellationToken)
    {
        var scope = Root().CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            using var work = Ambient.Enter(scope.ServiceProvider);
            await call(Of(scope.ServiceProvider), message, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<TResult> Alone<TMessage, TResult>(
        TMessage message,
        Func<IDispatcher, TMessage, CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken)
    {
        var scope = Root().CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            using var work = Ambient.Enter(scope.ServiceProvider);
            return await call(Of(scope.ServiceProvider), message, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IServiceProvider Root() =>
        _root ?? throw new InvalidOperationException(
            "Dispatcher was called before the app started, so there is no app to reach. Call it once the host is "
            + "built, or inject IDispatcher where a scope is already to hand.");
}
