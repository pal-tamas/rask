namespace Rask.Wire;

/// <summary>The outcome of a register or sign-in attempt.</summary>
/// <param name="Succeeded">Whether the attempt succeeded.</param>
/// <param name="Error">Why it did not, when it did not.</param>
/// <param name="Message">
/// A human-readable detail for the cases that carry one — a password-policy failure names what was
/// missing. <c>null</c> whenever <see cref="Error"/> alone says everything.
/// </param>
public sealed record AuthResult(bool Succeeded, AuthError Error = AuthError.None, string? Message = null)
{
    /// <summary>A successful attempt.</summary>
    public static AuthResult Success { get; } = new(true);

    /// <summary>A failed attempt.</summary>
    /// <param name="error">Why it failed.</param>
    /// <param name="message">An optional human-readable detail.</param>
    public static AuthResult Fail(AuthError error, string? message = null) => new(false, error, message);
}
