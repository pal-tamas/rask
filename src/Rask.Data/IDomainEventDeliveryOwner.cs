namespace Rask.Data;

/// <summary>
/// Registered by a component that takes ownership of delivering domain events, so that
/// <see cref="DomainEventInterceptor"/> stands down instead of publishing them in-process as well.
/// <c>Rask.Outbox</c>'s <c>AddRaskOutbox</c> registers one; an application with a delivery mechanism of
/// its own can register another.
/// </summary>
/// <remarks>
/// This exists so the choice is made <b>when the container is built</b> rather than when
/// <c>AddRaskData</c> is called. Deciding at registration time — the older
/// <c>AddRaskData(o =&gt; o.DispatchDomainEventsInProcess = false)</c> dance — froze the answer before
/// <c>AddRaskOutbox</c> had necessarily run, so it depended on the order of two calls in
/// <c>Program.cs</c> and silently did the wrong thing when they were swapped or when the second was
/// simply forgotten. The failure was invisible: <see cref="DomainEventInterceptor"/> drains and clears
/// every entity's events before the outbox interceptor can copy them, leaving the outbox table
/// permanently empty while the handlers still ran in-process, so delivery stopped being durable and
/// nothing reported an error.
/// <para>
/// Implementations carry no behaviour — presence in the container is the whole signal.
/// </para>
/// </remarks>
public interface IDomainEventDeliveryOwner
{
    /// <summary>
    /// A short name for the owner, used in diagnostics and log messages to say what took delivery.
    /// </summary>
    string Name { get; }
}
