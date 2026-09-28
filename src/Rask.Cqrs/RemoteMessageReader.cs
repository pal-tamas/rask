using System.Text.Json;
using Rask.Wire;

namespace Rask.Cqrs;

/// <summary>
///     Rebuilds a message from JSON, resolving each file index written by
///     <see cref="RemoteMessageWriter" /> against <paramref name="files" />.
/// </summary>
/// <param name="reader">A reader positioned at the message object.</param>
/// <param name="files">The files that arrived alongside the JSON, in index order.</param>
/// <returns>The reconstructed message.</returns>
public delegate object RemoteMessageReader(ref Utf8JsonReader reader, IReadOnlyList<RemoteFile> files);
