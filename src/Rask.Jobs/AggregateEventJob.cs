using System.Text.Json;
using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
///     The job an aggregate's <c>static Task On(E e)</c> runs as, once its <c>Configure</c> declared <c>b.Job&lt;E&gt;()</c>:
///     the handler's stored name and the event, as the outbox stores them.
/// </summary>
/// <param name="Handler">The durable handler's stored name — <c>{Aggregate}:{Event}</c>.</param>
/// <param name="Event">The event's stored type name.</param>
/// <param name="Payload">The event, as JSON.</param>
internal sealed record AggregateEventJob(string Handler, string Event, string Payload) : IJob
{
    /// <summary>The name its rows carry, kept apart from any app's own job names.</summary>
    public const string TypeName = "Rask.Background.AggregateEventJob";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static int _installed;

    /// <summary>The job that hands <paramref name="e" /> to <paramref name="handler" />.</summary>
    public static AggregateEventJob For(string handler, IEvent e)
    {
        var type = e.GetType();
        return new AggregateEventJob(handler, CqrsRegistry.NameOf(type), JsonSerializer.Serialize(e, type, Json));
    }

    /// <summary>Makes the job known: its rows read back, and it runs. Once per process.</summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1)
        {
            return;
        }

        JobSerializerRegistry.RegisterJob(TypeName, typeof(AggregateEventJob));
        CqrsRegistry.RegisterRequest(typeof(AggregateEventJob), static (provider, request, ct) => ((AggregateEventJob)request).Run(provider, ct));
    }

    private async Task Run(IServiceProvider provider, CancellationToken cancellationToken)
    {
        var type = CqrsRegistry.FindDurableEvent(Event)
            ?? throw new InvalidOperationException(
                $"No durable handler takes the event type '{Event}' any more: it was renamed or deleted after this job was enqueued.");
        var e = JsonSerializer.Deserialize(Payload, type, Json) as IEvent
            ?? throw new InvalidOperationException($"The event of this job does not read as '{Event}'.");
        await AggregateEvents.Run(provider, Handler, e, cancellationToken).ConfigureAwait(false);
    }
}
