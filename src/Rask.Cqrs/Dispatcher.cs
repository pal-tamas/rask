using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

/// <summary>
/// The default <see cref="IDispatcher"/>. Looks each request's concrete type up in
/// <see cref="CqrsRegistry"/> and invokes the source-generated, closed-generic pipeline — no
/// reflection. Registered transient so it captures whatever <see cref="IServiceProvider"/> constructs
/// it: the per-session scope on the Rask Server host, or the single root scope on WASM.
/// </summary>
internal sealed class Dispatcher(IServiceProvider provider) : IDispatcher
{

    private NotificationFeed? _feed;
    private CqrsExecutionOptions? _subscriptions;
    private IRemoteSubscriptions? _remote;
    private bool _remoteResolved;

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

        // Use the runtime type so a base-typed reference still reaches the right handlers and subscribers.
        var type = notification.GetType();

        // Subscribers first: the event has happened, and a screen should not wait on a slow handler to show it. On a
        // client whose notifications travel to the server, this tab's subscriptions are open ON the server, and hear
        // this one when the server does — delivering it here too would show it twice.
        if (!TravelsToServer(type) && Feed is { } feed)
        {
            feed.Publish(notification);
        }

        // A notification with no handlers has no generated invoker, and reaches only its subscribers.
        var invoker = CqrsRegistry.GetNotificationInvoker(type);
        return invoker is null ? Task.CompletedTask : invoker(provider, notification, cancellationToken);
    }

    // Iterators rather than wrappers around one: [EnumeratorCancellation] is what carries a token handed to
    // GetAsyncEnumerator into the watch, and a plain method returning the inner iterator would drop it — leaving a
    // subscription nobody can end.
    public async IAsyncEnumerable<TNotification> SubscribeAsync<TNotification>(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        await foreach (var notification in
                       Watch(typeof(TNotification), subscription: null, connected: null, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return (TNotification)notification;
        }
    }

    public async IAsyncEnumerable<TNotification> SubscribeAsync<TNotification>(
        ISubscription<TNotification> subscription,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(subscription);
        await foreach (var notification in
                       Watch(typeof(TNotification), subscription, connected: null, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return (TNotification)notification;
        }
    }

    /// <summary>
    ///     The notifications <paramref name="subscription" /> asked for — or every one of
    ///     <paramref name="notificationType" /> when it is null — from the server when this is a client of one, else from
    ///     this container's feed. The watch policy admits it first, and it starts with the most recent one published.
    ///     <paramref name="connected" /> runs once the subscription is open.
    /// </summary>
    internal async IAsyncEnumerable<INotification> Watch(
        Type notificationType,
        object? subscription,
        Action? connected,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var registration = subscription is null ? null : Registration(subscription.GetType());
        var type = registration?.NotificationType ?? notificationType;

        if (Remote is { } remote && ContractFor(subscription, type) is { } contract)
        {
            // The server admits or refuses it, with the signed-in user's policy; the browser's opinion is worth nothing.
            await foreach (var notification in remote.Subscribe(contract, subscription, connected, cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return notification;
            }

            yield break;
        }

        if (subscription is not null && registration is not null
            && !await registration.CanWatch(provider, subscription, cancellationToken).ConfigureAwait(false))
        {
            var name = subscription.GetType().Name;
            throw new UnauthorizedAccessException(
                $"Not allowed to open {name}. An IWatchPolicy<{name}> decides who may; with none registered, nobody may.");
        }

        var feed = Feed ?? throw new InvalidOperationException("Subscribing needs AddRaskCqrs() at startup.");
        var channel = Channel.CreateBounded<INotification>(new BoundedChannelOptions(Subscriptions.SubscriptionBuffer)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        var matches = subscription is null || registration is null
            ? null
            : new Func<INotification, bool>(notification => registration.Matches(subscription, notification));

        using var listening = feed.Listen(type, matches, notification => channel.Writer.TryWrite(notification));

        // The replay first, then "connected": whoever renders on connected already holds the latest value, so a
        // page's first paint shows it rather than an empty live state.
        while (channel.Reader.TryRead(out var replayed))
        {
            yield return replayed;
        }

        connected?.Invoke();

        await foreach (var notification in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return notification;
        }
    }

    // What this subscription crosses the wire as: its own contract when it is a record, else the notification's.
    private static RemoteContract? ContractFor(object? subscription, Type notificationType) =>
        subscription is null
            ? NotificationWire.ContractFor(notificationType)
            : NotificationWire.SubscriptionContractFor(subscription.GetType());

    private static SubscriptionRegistration Registration(Type subscriptionType) =>
        CqrsRegistry.FindSubscription(subscriptionType)
        ?? throw new InvalidOperationException(
            $"{subscriptionType.Name} is not registered as a subscription. The Rask.Cqrs generator records every "
            + "ISubscription<T> in the assembly that declares it — check that assembly references Rask.Cqrs.");

    private NotificationFeed? Feed => _feed ??= provider.GetService<NotificationFeed>();

    /// <summary>The subscription knobs from <c>Rask:Cqrs</c>, or their defaults outside a configured container.</summary>
    internal CqrsExecutionOptions Subscriptions =>
        _subscriptions ??= provider.GetService<CqrsExecutionOptions>() ?? CqrsExecutionOptions.Default;

    private IRemoteSubscriptions? Remote
    {
        get
        {
            if (!_remoteResolved)
            {
                _remote = provider.GetService<IRemoteSubscriptions>();
                _remoteResolved = true;
            }

            return _remote;
        }
    }

    private bool TravelsToServer(Type type) => Remote is not null && NotificationWire.ContractFor(type) is not null;
}
