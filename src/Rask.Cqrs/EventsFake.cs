using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

/// <summary>Every event a test published, and the sentences that ask about them.</summary>
public sealed class EventsFake : IDisposable
{
    private readonly List<Recording> _published = [];
    private readonly EventsFake? _previous;
    private readonly Lock _gate = new();

    // The event Run() is publishing for real: let through once, while what its handlers publish is recorded again.
    private readonly AsyncLocal<IEvent?> _releasing = new();
    private int _ran;
    private bool _disposed;

    internal EventsFake()
    {
        _previous = Events.Faked.Value;
        Events.Faked.Value = this;
        Events.Opened();
    }

    /// <summary>
    ///     Asks about what was published: <c>events.Published&lt;OrderPlaced&gt;().Once()</c>,
    ///     <c>events.Published&lt;OrderPlaced&gt;().Where(e =&gt; e.Id == id).Times(2)</c>,
    ///     <c>events.Published&lt;OrderCancelled&gt;().Never()</c>. <c>All</c> and <c>Only()</c> hand back the events
    ///     themselves.
    /// </summary>
    /// <typeparam name="TEvent">The event to ask about; a base type or interface counts every event it covers.</typeparam>
    public PublishedEvents<TEvent> Published<TEvent>()
        where TEvent : IEvent
    {
        lock (_gate)
        {
            return new PublishedEvents<TEvent>([.. _published.Select(static r => r.Event)]);
        }
    }

    /// <summary>How many events of every kind were published.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _published.Count;
            }
        }
    }

    /// <summary>
    ///     Publishes every recorded event that has not been published yet, for real — its subscriptions, then its
    ///     handlers: <c>await events.Run();</c>. An event a running handler publishes is recorded and published in the same
    ///     call. A handler that throws lets its exception out.
    /// </summary>
    /// <param name="cancellationToken">Cancels the handlers.</param>
    public async Task Run(CancellationToken cancellationToken = default)
    {
        while (Next() is { } next)
        {
            _releasing.Value = next.Event;
            try
            {
                await Publish(next, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _releasing.Value = null;
            }
        }
    }

    /// <summary>Forgets everything recorded, without putting real publishing back.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _published.Clear();
            _ran = 0;
        }
    }

    /// <summary>Puts real publishing back.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Events.Faked.Value = _previous;
        Events.Closed();
    }

    internal bool Record(IEvent e, IServiceProvider? services)
    {
        if (ReferenceEquals(_releasing.Value, e))
        {
            return false;
        }

        var scopes = services?.GetService<IServiceScopeFactory>();
        lock (_gate)
        {
            _published.Add(new Recording(e, scopes));
        }

        return true;
    }

    // From a scope of the app that published it, as the work it was published in had one: that work's own scope may be
    // gone by now (a save publishes its domain events from a scope it disposes). With none, the app the static reaches.
    private static async Task Publish(Recording next, CancellationToken cancellationToken)
    {
        if (next.Scopes is not { } scopes)
        {
            await Dispatcher.Publish(next.Event, cancellationToken).ConfigureAwait(false);
            return;
        }

        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            using var work = Ambient.Enter(scope.ServiceProvider);
            await scope.ServiceProvider.GetRequiredService<IDispatcher>().Publish(next.Event, cancellationToken).ConfigureAwait(false);
        }
    }

    private Recording? Next()
    {
        lock (_gate)
        {
            return _ran < _published.Count ? _published[_ran++] : null;
        }
    }

    private sealed record Recording(IEvent Event, IServiceScopeFactory? Scopes);
}
