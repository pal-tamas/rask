using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics;

namespace Rask.Core.Live;

/// <summary>
///     The services of the live event in progress: the app's scoped services from a scope of the event's own, opened
///     on first use and disposed when the event ends, and everything else from the session.
/// </summary>
/// <remarks>
///     One per session, so making it ambient allocates nothing: an event that resolves nothing of the app's opens no
///     scope. Holding it is safe — it answers for whichever event is in progress — while holding what it handed out
///     past the event is not.
/// </remarks>
internal sealed class EventServices(LiveSessionBase session, EventScopeCatalog catalog, IServiceScopeFactory scopes)
    : IKeyedServiceProvider
{
    private readonly Lock _gate = new();
    private AsyncServiceScope? _scope;

    // Whether a scope is open, read without the gate by the end of an event that opened none: every event's path.
    private volatile bool _open;

    /// <summary>The scope the event in progress has opened, or null when it has resolved nothing of the app's.</summary>
    internal IServiceProvider? Opened
    {
        get
        {
            lock (_gate)
            {
                return _scope?.ServiceProvider;
            }
        }
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
        {
            return this;
        }

        return catalog.OwnsPerEvent(serviceType)
            ? Open().GetService(serviceType)
            : session.Services.GetService(serviceType);
    }

    public object? GetKeyedService(Type serviceType, object? serviceKey) =>
        (session.Services as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
        session.Services.GetRequiredKeyedService(serviceType, serviceKey);

    private IServiceProvider Open()
    {
        lock (_gate)
        {
            if (_scope is { } open)
            {
                return open.ServiceProvider;
            }

            var scope = scopes.CreateAsyncScope();
            try
            {
                session.SeedEventScope(scope.ServiceProvider);
            }
            catch
            {
                _ = DisposeAsync(scope);
                throw;
            }

            _scope = scope;
            _open = true;
            return scope.ServiceProvider;
        }
    }

    /// <summary>
    ///     Ends the event's scope: disposed now, or when <paramref name="stillReading" /> — the reads the event's page
    ///     went out without — are back, since they hold what they resolved from it.
    /// </summary>
    internal void Close(Task? stillReading)
    {
        if (!_open)
        {
            return;
        }

        AsyncServiceScope scope;
        lock (_gate)
        {
            if (_scope is not { } open)
            {
                return;
            }

            scope = open;
            _scope = null;
            _open = false;
        }

        _ = stillReading is null ? DisposeAsync(scope) : DisposeAfterAsync(stillReading, scope);
    }

    private static async Task DisposeAfterAsync(Task reads, AsyncServiceScope scope)
    {
        await reads.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await DisposeAsync(scope).ConfigureAwait(false);
    }

    private static async Task DisposeAsync(AsyncServiceScope scope)
    {
        try
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A service of the app's that throws as it is disposed must not take the session with it: reported.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            RaskDiagnostics.Report(RaskLogLevel.Error, "Rask.Live", "Rask Live: disposing an event's services threw", ex);
        }
    }
}
