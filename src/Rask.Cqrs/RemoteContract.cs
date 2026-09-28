using Rask.Wire;

namespace Rask.Cqrs;

/// <summary>
///     Everything the transports need to move one message across a process boundary: what to call it
///     on the wire, which HTTP shape it takes, and how to encode it — with no reflection at any point.
///     Emitted by the Rask.Cqrs source generator; you do not construct one.
/// </summary>
public sealed class RemoteContract
{
    /// <summary>The message's CLR type — the key both transports look it up by.</summary>
    public required Type MessageType { get; init; }

    /// <summary>
    ///     The name in the request path. Defaults to the message's full type name; a message that is
    ///     renamed after release should pin its old name to keep the wire compatible.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Which message shape this is, and therefore which verb it uses.</summary>
    public required RemoteMessageKind Kind { get; init; }

    /// <summary>
    ///     The result's CLR type — <see cref="Unit" /> for a void command or a notification.
    /// </summary>
    public required Type ResultType { get; init; }

    /// <summary>Encodes the message for sending.</summary>
    public required RemoteMessageWriter WriteMessage { get; init; }

    /// <summary>Decodes a received message.</summary>
    public required RemoteMessageReader ReadMessage { get; init; }

    /// <summary>
    ///     Encodes the handler's result. Null when <see cref="ResultType" /> is <see cref="Unit" /> or
    ///     <see cref="ReturnsFile" /> is true — neither is encoded as JSON.
    /// </summary>
    public RemoteResultWriter? WriteResult { get; init; }

    /// <summary>Decodes a received result. Null for the same cases as <see cref="WriteResult" />.</summary>
    public RemoteResultReader? ReadResult { get; init; }

    /// <summary>
    ///     True when the message carries one or more <see cref="RemoteFile" /> values, so it must be
    ///     sent as multipart rather than as a JSON body — and, being a body, never as a GET.
    /// </summary>
    public bool CarriesFiles { get; init; }

    /// <summary>
    ///     True when the result is a <see cref="FileDownload" />, so the response is a streamed body
    ///     with a <c>Content-Disposition</c> rather than a JSON document.
    /// </summary>
    public bool ReturnsFile { get; init; }

    /// <summary>
    ///     Runs this message against its local handler, boxing the result so an endpoint that only knows
    ///     the message as <see cref="object" /> can serialize it.
    /// </summary>
    /// <remarks>
    ///     The mirror of <see cref="Invoker" /> and generated for the same reason: pulling a result out
    ///     of a <c>Task&lt;TResult&gt;</c> without knowing <c>TResult</c> would need reflection. It goes
    ///     through <see cref="IDispatcher" /> rather than around it, so pipeline behaviors still wrap the
    ///     handler. Only a server calls this — a client installs remote invokers, where routing a
    ///     received message back through the dispatcher would send it out again.
    /// </remarks>
    public RemoteLocalInvoker? LocalInvoker { get; init; }

    /// <summary>
    ///     The authorization policy the handler declared with <c>[Authorize(Policy = …)]</c>, or null.
    /// </summary>
    public string? Policy { get; init; }

    /// <summary>
    ///     The roles the handler declared with <c>[Authorize(Roles = …)]</c>, comma-separated, or null.
    ///     Read as well as <see cref="Policy" /> because ignoring it would be worse than not supporting
    ///     it: an author who wrote <c>[Authorize(Roles = "admin")]</c> would believe it was enforced.
    /// </summary>
    public string? Roles { get; init; }

    /// <summary>
    ///     True when the handler is marked <c>[AllowAnonymous]</c>, which is the only way past the
    ///     endpoint's authenticated-by-default rule.
    /// </summary>
    public bool AllowAnonymous { get; init; }

    /// <summary>
    ///     True when the record itself carries <c>[Authorize]</c> or <c>[AllowAnonymous]</c>, which is what opens a
    ///     notification subscribed to <em>by type</em> to remote subscribers. An <see cref="ISubscription{TNotification}" />
    ///     record is guarded by its <see cref="IWatchPolicy{TSubscription}" /> instead and needs no declaration; a
    ///     notification that declares nothing stays closed to bare subscribers, so no auth event is ever one browser
    ///     request away.
    /// </summary>
    public bool SubscribeDeclared { get; init; }

    /// <summary>The policy the record names with <c>[Authorize(Policy = …)]</c>, for subscribers.</summary>
    public string? SubscribePolicy { get; init; }

    /// <summary>The roles the record names with <c>[Authorize(Roles = …)]</c>, for subscribers.</summary>
    public string? SubscribeRoles { get; init; }

    /// <summary>True when the record is marked <c>[AllowAnonymous]</c>: a signed-out visitor may subscribe.</summary>
    public bool SubscribeAnonymously { get; init; }

    /// <summary>
    ///     Sends this message through the ambient <see cref="IRemoteDispatch" />, returning the
    ///     <c>Task&lt;TResult&gt;</c> the dispatcher expects.
    /// </summary>
    /// <remarks>
    ///     Generated, and closed over the concrete result type — which is the only way to produce a typed
    ///     task without <c>MakeGenericType</c>. A client transport installs this into
    ///     <see cref="CqrsRegistry" /> so that dispatching the message reaches the server instead of a
    ///     local handler; a server leaves it alone and dispatches in-process.
    /// </remarks>
    public CqrsRegistry.RequestInvoker? Invoker { get; init; }
}
