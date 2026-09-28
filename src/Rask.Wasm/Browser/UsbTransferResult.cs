namespace Rask.Wasm.Browser;

/// <summary>Result of an inbound transfer.</summary>
/// <param name="Status"><c>"ok"</c>, <c>"stall"</c>, or <c>"babble"</c>.</param>
/// <param name="Data">The bytes received.</param>
public sealed record UsbTransferResult(string Status, byte[] Data);
