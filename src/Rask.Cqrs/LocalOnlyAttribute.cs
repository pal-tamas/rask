namespace Rask.Cqrs;

/// <summary>
///     Keeps a message off the wire. Applied to a message type, or to a handler class, it means: no
///     generated codec, no endpoint, never dispatched remotely — this message exists only inside the
///     process that declares it.
/// </summary>
/// <remarks>
///     <para>
///         Rask.Cqrs exposes a handler's message remotely by default, so that a hosted app's client and
///         server share one message vocabulary with nothing extra to declare. That default is right for
///         the messages an app's own UI sends, and wrong for the ones it doesn't: a command that only
///         another handler publishes, a job payload, an outbox event, anything whose caller is always
///         in-process. Mark those <see cref="LocalOnlyAttribute" /> and they are unreachable from
///         outside — the endpoint answers 404 for a name it was never given.
///     </para>
///     <para>
///         It has a second, quieter use. Generated codecs constrain what a message may look like (see
///         RASK053), because a shape with no wire encoding cannot be sent. A local-only message is
///         never encoded, so it is free to carry whatever its handler finds convenient — an interface,
///         a domain entity, a delegate.
///     </para>
///     <para>
///         Applying it to an <b>interface</b> marks every message that implements it, which is how a
///         family of always-in-process messages opts out at once: <c>Rask.Jobs</c>' <c>IJob</c> and
///         <c>Rask.Outbox</c>' <c>IOutboxEvent</c> both derive from <see cref="ICommand" />, and neither
///         a job payload nor an outbox event is ever something a browser sends.
///     </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface,
    Inherited = false)]
public sealed class LocalOnlyAttribute : Attribute;
