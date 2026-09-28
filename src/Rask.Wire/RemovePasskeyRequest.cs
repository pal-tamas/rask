namespace Rask.Wire;

/// <summary>The passkey to remove.</summary>
/// <param name="Id">Its id, as <c>/me</c> and the device list report it.</param>
public sealed record RemovePasskeyRequest(string Id);
