using System.ComponentModel;

namespace Rask.Core.Browser;

/// <summary>
///     One message as it crosses the interop boundary — binary rides base64-encoded, because
///     <c>byte[]</c> doesn't marshal.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record RtcMessageWire(string? Text, string? Data);
