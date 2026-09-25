namespace Rask.Wire;

/// <summary>An address to send a password-reset link to.</summary>
/// <param name="Email">The email address.</param>
public sealed record ForgotPasswordRequest(string Email);
