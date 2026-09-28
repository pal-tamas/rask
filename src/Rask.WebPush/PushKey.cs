namespace Rask.WebPush;

/// <summary>What <c>GET /_rask/push/key</c> answers.</summary>
/// <param name="PublicKey">The VAPID public key, or an empty string until one is configured.</param>
public sealed record PushKey(string PublicKey);
