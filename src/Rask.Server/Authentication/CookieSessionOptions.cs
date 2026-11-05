namespace Rask.Server;

/// <summary>
///     The session cookie a sign-in with the app's own token is written into (<c>Rask:CookieSession</c>). It is always
///     <c>HttpOnly</c>, <c>Secure</c>, <c>SameSite=Lax</c> and set at <c>Path=/</c>: those are not knobs.
/// </summary>
public sealed class CookieSessionOptions
{
    /// <summary>The cookie's name — the one the app's authentication scheme reads its token from. <c>rask.session</c> by default.</summary>
    public string Name { get; set; } = "rask.session";

    /// <summary>
    ///     The cookie's <c>Domain</c>, for a session shared with the site's other hosts (<c>.example.com</c>). Unset by
    ///     default, which keeps the cookie to this host alone.
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>
    ///     How long the cookie outlives the sign-in. Unset by default, which ends it when the browser closes; set it to the
    ///     life of the app's own token (<c>7.Days</c>), so the cookie never outlasts what it carries.
    /// </summary>
    public TimeSpan? Lifetime { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Any(static c => c <= ' ' || c >= 0x7F || "()<>@,;:\\\"/[]?={}".Contains(c)))
        {
            throw new InvalidOperationException(
                $"Rask:CookieSession:Name '{Name}' is not a cookie name: use letters, digits and - . _ only.");
        }

        if (Lifetime is { } lifetime && lifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"Rask:CookieSession:Lifetime must be positive, or unset for a cookie that ends with the browser; it is {lifetime}.");
        }
    }
}
