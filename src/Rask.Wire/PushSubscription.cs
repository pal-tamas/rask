namespace Rask.Wire;

/// <summary>
/// A browser's push subscription: what <c>pushManager.subscribe</c> hands back, and what a server encrypts a
/// push for. One type on both sides of the wire, so a component on the server host stores exactly what the
/// browser API produced and a WebAssembly client posts exactly what the server reads.
/// </summary>
/// <param name="Endpoint">The push service URL a message is POSTed to.</param>
/// <param name="P256dh">The browser's P-256 ECDH public key, base64url — the payload is encrypted for it (RFC 8291).</param>
/// <param name="Auth">The browser's 16-byte auth secret, base64url (RFC 8291).</param>
/// <param name="ExpirationTime">When the subscription lapses, as milliseconds since the epoch; <see langword="null" /> when the push service set none.</param>
public sealed record PushSubscription(string Endpoint, string P256dh, string Auth, double? ExpirationTime = null);
