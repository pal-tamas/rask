using Microsoft.Extensions.DependencyInjection;
using Rask.Batteries;
using Rask.Data;

namespace Rask.Cqrs;

/// <summary>Every event a test stored for its durable handlers, and the sentences that ask about them.</summary>
public sealed class OutboxFake : IDisposable
{
    private readonly List<IEvent> _stored = [];
    private readonly List<Pending> _pending = [];
    private readonly OutboxFake? _previous;
    private readonly Lock _gate = new();
    private int _ran;

    internal OutboxFake()
    {
        _previous = Outbox.Faked.Value;
        Outbox.Faked.Value = this;
    }

    /// <summary>
    ///     Asks about what was stored: <c>outbox.Stored&lt;OrderPlaced&gt;().Once()</c>,
    ///     <c>outbox.Stored&lt;OrderPlaced&gt;().None()</c>. An event counts once, however many durable handlers
    ///     it has. <c>Only()</c> hands back the one that matched, to assert on the event itself.
    /// </summary>
    /// <typeparam name="TEvent">The event to ask about.</typeparam>
    public Counting<TEvent> Stored<TEvent>()
        where TEvent : IEvent
    {
        lock (_gate)
        {
            // What was stored INSTEAD — the half of a failing expectation that says where to look.
            var others = _stored.Count == 0
                ? "Nothing was stored at all."
                : $"Stored instead: {string.Join(", ", _stored.Select(e => e.GetType().Name))}.";

            return new Counting<TEvent>(
                [.. _stored.OfType<TEvent>()], typeof(TEvent).Name, "stored", static e => e.ToString() ?? "", others);
        }
    }

    /// <summary>
    ///     Runs every recorded durable handler that has not run yet, for real, as the tenant and user who
    ///     raised its event: <c>await outbox.Run();</c>. An event a running handler publishes is recorded and
    ///     its durable handlers run in the same call. A handler that throws lets its exception out.
    /// </summary>
    public async Task Run(CancellationToken cancellationToken = default)
    {
        while (Next() is { } next)
        {
            var scope = next.Scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                using var tenant = next.Tenant is { } owner ? Tenant.Use(owner) : null;
                using var user = Current.UseUser(next.User);

                // Work in progress, as the processor makes it: `Jobs.Enqueue` and `Mail.Send` reach the app from here.
                using var work = Ambient.Enter(scope.ServiceProvider);
                using var cancellation = Ambient.Enter(cancellationToken);
                var handler = CqrsRegistry.FindDurableHandler(next.Handler)
                    ?? throw new InvalidOperationException($"No durable handler '{next.Handler}'.");
                await handler(scope.ServiceProvider, next.Event, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Forgets everything recorded, without putting the real outbox back.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _stored.Clear();
            _pending.Clear();
            _ran = 0;
        }
    }

    /// <summary>Puts the real outbox back.</summary>
    public void Dispose() => Outbox.Faked.Value = _previous;

    /// <summary>Records <paramref name="e" /> once per durable handler, where the outbox would have written a row each.</summary>
    internal void Record(IEvent e, IReadOnlyList<string> handlers, IServiceScopeFactory scopes)
    {
        lock (_gate)
        {
            _stored.Add(e);
            _pending.AddRange(handlers.Select(handler => new Pending(e, handler, Current.Tenant, Current.UserId, scopes)));
        }
    }

    private Pending? Next()
    {
        lock (_gate)
        {
            return _ran < _pending.Count ? _pending[_ran++] : null;
        }
    }

    private sealed record Pending(IEvent Event, string Handler, Guid? Tenant, Guid? User, IServiceScopeFactory Scopes);
}
