namespace Rask.WebPush;

// The browser push-subscription handle your backend needs to deliver a message. This is the
// server-side mirror of the client's PushSubscription (Rask.Wasm.Browser.IWebPush.SubscribeAsync) —
// the client POSTs these three fields to you as JSON and you store them. Defined here as the
// package's own type so Rask.WebPush stays a standalone server library with no reference to
// Rask.Wasm/Rask.Core; the two sides simply agree on the wire shape.
//
//   Endpoint — the push service URL to POST the encrypted message to.
//   P256dh   — base64url of the client's P-256 ECDH public key (payload encryption, RFC 8291).
//   Auth     — base64url of the client's 16-byte auth secret (payload encryption, RFC 8291).
/// <summary>
///     One browser's push subscription — everything the server needs to deliver a message to it. The
///     client subscribes, posts these three fields to you as JSON, and you store them against the user.
/// </summary>
/// <remarks>
///     Treat a stored subscription as a credential for reaching that person's device: it is enough to
///     push to them, so scope it to its user and drop it when they sign out or unsubscribe.
///     <para>
///         Subscriptions expire on their own — a browser can revoke one at any time. A send whose result
///         reports <see cref="WebPushResult.ShouldDelete" /> means this row is dead: remove it from your
///         store rather than retry it.
///     </para>
/// </remarks>
/// <param name="Endpoint">The push service URL the encrypted message is posted to.</param>
/// <param name="P256dh">The client's P-256 ECDH public key, base64url — payload encryption (RFC 8291).</param>
/// <param name="Auth">The client's 16-byte auth secret, base64url — payload encryption (RFC 8291).</param>
public sealed record PushSubscription(string Endpoint, string P256dh, string Auth);
