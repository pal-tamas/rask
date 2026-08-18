using System.Text.Json;

namespace Rask.Cqrs;

/// <summary>Which of the four message shapes a remote contract describes.</summary>
public enum RemoteMessageKind
{
    /// <summary>An <see cref="IQuery{TResult}" /> — safe and idempotent, so it travels as a GET.</summary>
    Query,

    /// <summary>An <see cref="ICommand" /> — travels as a POST and answers with no value.</summary>
    VoidCommand,

    /// <summary>An <see cref="ICommand{TResult}" /> — travels as a POST and answers with a value.</summary>
    ResultCommand,

    /// <summary>An <see cref="INotification" /> — travels as a POST and is accepted, not answered.</summary>
    Notification,
}

/// <summary>
///     Writes a message as JSON, collecting any <see cref="RemoteFile" /> it carries into
///     <paramref name="files" /> and writing each one's index in their place.
/// </summary>
/// <param name="writer">The JSON writer to append the message object to.</param>
/// <param name="message">The message instance.</param>
/// <param name="files">Receives the files, in the order their indices were written.</param>
public delegate void RemoteMessageWriter(Utf8JsonWriter writer, object message, IList<RemoteFile> files);

/// <summary>
///     Rebuilds a message from JSON, resolving each file index written by
///     <see cref="RemoteMessageWriter" /> against <paramref name="files" />.
/// </summary>
/// <param name="reader">A reader positioned at the message object.</param>
/// <param name="files">The files that arrived alongside the JSON, in index order.</param>
/// <returns>The reconstructed message.</returns>
public delegate object RemoteMessageReader(ref Utf8JsonReader reader, IReadOnlyList<RemoteFile> files);

/// <summary>Writes a message's result as JSON.</summary>
/// <param name="writer">The JSON writer.</param>
/// <param name="result">The value the handler returned; may be null for a nullable result type.</param>
public delegate void RemoteResultWriter(Utf8JsonWriter writer, object? result);

/// <summary>Reads a message's result from JSON.</summary>
/// <param name="reader">A reader positioned at the result value.</param>
/// <returns>The decoded result.</returns>
public delegate object? RemoteResultReader(ref Utf8JsonReader reader);

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
}
