namespace Rask.Cqrs;

/// <summary>
/// The default <see cref="IDispatcher"/>. Looks each request's concrete type up in
/// <see cref="CqrsRegistry"/> and invokes the source-generated, closed-generic pipeline — no
/// reflection. Registered transient so it captures whatever <see cref="IServiceProvider"/> constructs
/// it: the per-session scope on the Rask Server host, or the single root scope on WASM.
/// </summary>
internal sealed class LocalDispatcher(IServiceProvider provider) : IDispatcher
{
    public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var invoker = CqrsRegistry.GetRequestInvoker(query.GetType());
        return (Task<TResult>)Run(invoker, query, cancellationToken);
    }

    public Task Send(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var invoker = CqrsRegistry.GetRequestInvoker(command.GetType());
        return Run(invoker, command, cancellationToken); // Task<Unit> is a Task
    }

    public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var invoker = CqrsRegistry.GetRequestInvoker(command.GetType());
        return (Task<TResult>)Run(invoker, command, cancellationToken);
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        // Use the runtime type so a base-typed reference still reaches the right handlers. A
        // notification with no handlers is a no-op (no generated invoker exists for it).
        var invoker = CqrsRegistry.GetNotificationInvoker(notification.GetType());
        return invoker is null ? Task.CompletedTask : Run(invoker, notification, cancellationToken);
    }

    // A handler takes only its message, so the work it belongs to travels ambiently: this scope's services, and
    // the token the caller passed — or, when it passed none, the one of the work that sent the message. Entered
    // around the call that STARTS the pipeline, so the async handler captures both and keeps them after it.
    private Task Run(CqrsRegistry.RequestInvoker invoker, object message, CancellationToken cancellationToken)
    {
        var token = Ambient.Or(cancellationToken);
        using var services = Ambient.Enter(provider);
        using var cancellation = Ambient.Enter(token);
        return invoker(provider, message, token);
    }

    private Task Run(CqrsRegistry.NotificationInvoker invoker, object message, CancellationToken cancellationToken)
    {
        var token = Ambient.Or(cancellationToken);
        using var services = Ambient.Enter(provider);
        using var cancellation = Ambient.Enter(token);
        return invoker(provider, message, token);
    }
}
