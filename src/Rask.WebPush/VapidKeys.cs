namespace Rask.WebPush;

// A VAPID application-server key pair (RFC 8292), both members base64url-encoded:
//   PublicKey  — the uncompressed P-256 point (65 bytes: 0x04 ‖ X ‖ Y). This is exactly the
//                `applicationServerKey` the browser passes to pushManager.subscribe, so hand the
//                SAME string to the client's IWebPush.SubscribeAsync.
//   PrivateKey — the 32-byte private scalar D. Keep it secret on the server.
//
// Generate one pair per application with Generate(), store it in configuration/secrets, and reuse it
// for the lifetime of the app — rotating it invalidates every existing subscription.
/// <summary>
///     The VAPID key pair identifying your application server to a push service (RFC 8292). Generate one
///     pair per application with <see cref="Generate" /> and reuse it for the life of the app.
/// </summary>
/// <remarks>
///     <see cref="PrivateKey" /> is a signing key: keep it in secrets or configuration the way you would a
///     database password, never in source control and never sent to the browser. <see cref="PublicKey" />
///     is meant to be public — it is exactly the <c>applicationServerKey</c> the browser subscribes with.
///     <para>
///         Rotating the pair invalidates every existing subscription, so every user is silently
///         unsubscribed until they subscribe again. Treat it as a migration, not a routine key rotation.
///     </para>
/// </remarks>
/// <param name="PublicKey">The uncompressed P-256 point, base64url-encoded. Hand this same string to the
///     client's <c>IWebPush.SubscribeAsync</c>.</param>
/// <param name="PrivateKey">The 32-byte private scalar, base64url-encoded. Server-side secret.</param>
public sealed record VapidKeys(string PublicKey, string PrivateKey)
{
    /// <summary>
    ///     Creates a fresh P-256 pair in the base64url form both the browser and this sender expect. Run
    ///     it once, store the result — calling it per send would invalidate every subscription each time.
    /// </summary>
    // Create a fresh P-256 key pair in the exact base64url form the browser and this sender expect.
    // The generation lives in VapidKeyMaterial so `rask new --push` can mint a development pair from
    // the same code without source-linking this public record into the tool.
    public static VapidKeys Generate()
    {
        (var publicKey, var privateKey) = VapidKeyMaterial.Generate();

        return new VapidKeys(publicKey, privateKey);
    }
}
