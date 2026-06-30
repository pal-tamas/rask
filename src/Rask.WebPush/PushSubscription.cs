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
public sealed record PushSubscription(string Endpoint, string P256dh, string Auth);
