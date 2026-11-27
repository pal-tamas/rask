using Rask.Wire;

namespace Rask.Auth;

/// <summary>What <see cref="IBearerSignIn.SignIn" /> answers.</summary>
/// <param name="Result">Whether the name and password matched, and why not when they did not.</param>
/// <param name="Token">The bearer token, on a success; <see langword="null" /> otherwise.</param>
/// <param name="ExpiresAt">When the token and its session expire, on a success; <see langword="null" /> otherwise.</param>
public sealed record BearerIssue(AuthResult Result, string? Token, DateTimeOffset? ExpiresAt);
