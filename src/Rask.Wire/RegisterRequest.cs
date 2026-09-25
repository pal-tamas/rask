namespace Rask.Wire;

/// <summary>Credentials for a new account.</summary>
/// <param name="Email">The email address, which is also the user name.</param>
/// <param name="Password">The password.</param>
/// <param name="FirstRunToken">The first-run token, needed only while no account exists yet.</param>
public sealed record RegisterRequest(string Email, string Password, string? FirstRunToken = null);
