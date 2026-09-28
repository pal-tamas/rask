namespace Rask.Wire;

/// <summary>Credentials for an existing account.</summary>
/// <param name="Email">The email address.</param>
/// <param name="Password">The password.</param>
/// <param name="Remember">Whether the session should outlive the browser session.</param>
public sealed record LoginRequest(string Email, string Password, bool Remember = false);
