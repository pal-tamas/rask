namespace Rask.Cqrs;

/// <summary>
/// The default <see cref="IDispatcher"/>. Looks each request's concrete type up in
/// <see cref="CqrsRegistry"/> and invokes the source-generated, closed-generic pipeline — no
/// reflection. Registered transient so it captures whatever <see cref="IServiceProvider"/> constructs
/// it: the per-session scope on the Rask Server host, or the single root scope on WASM.
/// </summary>
internal sealed class Dispatcher(IServiceProvider provider) : IDispatcher
{
    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var invoker = CqrsRegistry.GetRequestInvoker(query.GetType());
        return (Task<TResult>)invoker(provider, query, cancellationToken);
    }

    public Task SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var invoker = CqrsRegistry.GetRequestInvoker(command.GetType());
        return invoker(provider, command, cancellationToken); // Task<Unit> is a Task
    }

    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var invoker = CqrsRegistry.GetRequestInvoker(command.GetType());
        return (Task<TResult>)invoker(provider, command, cancellationToken);
    }

    public Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        // Use the runtime type so a base-typed reference still reaches the right handlers. A
        // notification with no handlers is a no-op (no generated invoker exists for it).
        var invoker = CqrsRegistry.GetNotificationInvoker(notification.GetType());
        return invoker is null ? Task.CompletedTask : invoker(provider, notification, cancellationToken);
    }
}
