using System.Text.Json;
using Rask.Wire;

namespace Rask.Cqrs;

/// <summary>
///     Writes a message as JSON, collecting any <see cref="RemoteFile" /> it carries into
///     <paramref name="files" /> and writing each one's index in their place.
/// </summary>
/// <param name="writer">The JSON writer to append the message object to.</param>
/// <param name="message">The message instance.</param>
/// <param name="files">Receives the files, in the order their indices were written.</param>
public delegate void RemoteMessageWriter(Utf8JsonWriter writer, object message, IList<RemoteFile> files);
