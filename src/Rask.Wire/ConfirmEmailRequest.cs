namespace Rask.Wire;

/// <summary>An address to mark confirmed, and the emailed token that proves it.</summary>
/// <param name="UserId">The account the link named.</param>
/// <param name="Token">The token the link carried.</param>
public sealed record ConfirmEmailRequest(string UserId, string Token);
