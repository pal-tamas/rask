namespace Rask.Auth;

/// <summary>A claim an account gave for itself, as a session keeps it: plain text, with no identity attached.</summary>
internal sealed record OwnClaim(string Type, string Value);
