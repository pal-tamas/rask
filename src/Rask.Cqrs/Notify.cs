using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

/// <summary>
///     Publishes a notification from anywhere, with nothing injected — a background job, a hosted service, a
///     webhook, a domain method.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="IDispatcher" /> is registered transient and reaches its handlers through the provider that
///         constructed it, so publishing from a singleton — a <c>BackgroundService</c>, a timer — means opening a
///         scope by hand before anything can be sent. This facade does that part:
///         <code>
/// public sealed class ReportWorker : BackgroundService
/// {
///     protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
///         Notify.Send(new ReportReady(reportId), stoppingToken);
/// }
///         </code>
///     </para>
///     <para>
///         It is the same publish in every respect: subscribers hear it first, then the notification's handlers
///         run. Injecting <see cref="IDispatcher" /> where one is already to hand — a command handler, an
///         endpoint — stays exactly right, and is what the facade does underneath.
///     </para>
///     <para>
///         <b>Which scope the handlers get.</b> The one <see cref="UseScope" /> bound, when work in flight has
///         bound one; otherwise a fresh scope of its own, disposed once the handlers finish. Either way a handler
///         should take an <c>IDbContextFactory</c> rather than a scoped <c>DbContext</c>, as it must already:
///         nothing guarantees the scope it runs in outlives the publish. <c>Current.UserId</c> and the rest flow
///         on <see cref="AsyncLocal{T}" />, so they are unaffected by which scope this is.
///     </para>
/// </remarks>
public static class Notify
{
    private static readonly AsyncLocal<IServiceProvider?> AmbientScope = new();

    private static IServiceProvider? _root;

    /// <summary>Whether <see cref="Configure" /> has run and <see cref="Send" /> has somewhere to publish.</summary>
    public static bool IsConfigured => _root is not null || AmbientScope.Value is not null;

    /// <summary>
    ///     Supplies the provider <see cref="Send" /> opens its scopes from. A Rask app never calls it —
    ///     <c>AddRaskCqrs()</c> arranges it at startup.
    /// </summary>
    /// <param name="services">The application's root provider.</param>
    public static void Configure(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _root = services;
    }

    /// <summary>
    ///     Binds the scope <see cref="Send" /> publishes on until the returned handle is disposed, for work that
    ///     already has one — a live session, a request, a test.
    /// </summary>
    /// <param name="scope">The scope to publish on.</param>
    /// <returns>A handle that restores the previous scope.</returns>
    public static IDisposable UseScope(IServiceProvider scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new ScopeBinding(scope);
    }

    /// <summary>
    ///     Publishes <paramref name="notification" />: every subscription watching it is told, and every handler
    ///     for it runs.
    /// </summary>
    /// <typeparam name="TNotification">The notification being published.</typeparam>
    /// <param name="notification">What happened.</param>
    /// <param name="cancellationToken">Cancels the handlers; subscribers have already been told.</param>
    /// <returns>A task that completes when the handlers have.</returns>
    public static Task Send<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (AmbientScope.Value is { } bound)
        {
            return Publish(bound, notification, cancellationToken);
        }

        var root = _root ?? throw new InvalidOperationException(
            "Notify.Send was called before Rask.Cqrs started. Register it with services.AddRaskCqrs(), or "
            + "inject IDispatcher where the publish already has a scope.");

        return SendInOwnScope(root, notification, cancellationToken);
    }

    // Awaits the handlers before the scope goes: disposing it underneath them would take away the very services
    // they were given it for.
    private static async Task SendInOwnScope<TNotification>(
        IServiceProvider root,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        await using var scope = root.CreateAsyncScope();
        await Publish(scope.ServiceProvider, notification, cancellationToken).ConfigureAwait(false);
    }

    private static Task Publish<TNotification>(
        IServiceProvider services,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification =>
        services.GetService<IDispatcher>() is { } dispatcher
            ? dispatcher.PublishAsync(notification, cancellationToken)
            : throw new InvalidOperationException(
                "Notify.Send needs Rask.Cqrs registered: call services.AddRaskCqrs().");

    private sealed class ScopeBinding : IDisposable
    {
        private readonly IServiceProvider? _previous;
        private bool _disposed;

        internal ScopeBinding(IServiceProvider scope)
        {
            _previous = AmbientScope.Value;
            AmbientScope.Value = scope;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            AmbientScope.Value = _previous;
        }
    }
}
