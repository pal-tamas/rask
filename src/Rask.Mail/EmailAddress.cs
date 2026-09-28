namespace Rask.Mailing;

/// <summary>An email address with an optional display name.</summary>
/// <param name="Address">The email address (e.g. <c>jane@example.com</c>).</param>
/// <param name="Name">An optional display name (e.g. <c>Jane Doe</c>).</param>
public sealed record EmailAddress(string Address, string? Name = null);
