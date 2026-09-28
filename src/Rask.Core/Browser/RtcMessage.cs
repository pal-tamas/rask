namespace Rask.Core.Browser;

/// <summary>
///     One message received on a data channel. Exactly one of <see cref="Text" /> / <see cref="Data" /> is
///     set, matching what the peer sent.
/// </summary>
/// <param name="Text">The message, when the peer sent a string.</param>
/// <param name="Data">The message, when the peer sent binary.</param>
public sealed record RtcMessage(string? Text, byte[]? Data);
