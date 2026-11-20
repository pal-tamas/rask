namespace Rask.Auth.Client;

/// <summary>What <c>POST /_rask/push/release</c> reads: the subscription's endpoint.</summary>
internal sealed record PushReleaseRequest(string Endpoint);
