namespace Rask.Auth;

/// <summary>A reset asked for <paramref name="Address" />, its link to point at <paramref name="Origin" />.</summary>
internal sealed record PasswordResetRequest(string Address, string Origin);
