namespace Rask.Wire;

/// <summary>A new password, and the emailed token that authorizes setting it.</summary>
/// <param name="UserId">The account the link named.</param>
/// <param name="Token">The token the link carried.</param>
/// <param name="Password">The new password.</param>
public sealed record ResetPasswordRequest(string UserId, string Token, string Password);
