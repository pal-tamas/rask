namespace Rask.Auth;

/// <summary>
///     Renders the two emails the account lifecycle sends.
/// </summary>
/// <remarks>
///     <para>
///         HTML rather than a Rask component, and that is the point: <c>Email.Body(Component)</c> is
///         the one member of <c>Rask.Mail</c> that reaches into <c>Rask.Core</c>, so an accounts
///         battery that went through it could not run on a host with no renderer (#1069).
///     </para>
///     <para>
///         <c>Rask.Auth</c> replaces the default with one that renders the styled components it already
///         ships, so an app that IS a Rask app sends exactly the emails it sent before.
///     </para>
/// </remarks>
public interface IAuthEmailBodies
{
    /// <summary>The "confirm your email" body.</summary>
    /// <param name="link">The absolute confirmation link.</param>
    /// <param name="subject">The subject, repeated as the heading.</param>
    string Confirm(string link, string subject);

    /// <summary>The "reset your password" body.</summary>
    /// <param name="link">The absolute reset link.</param>
    /// <param name="subject">The subject, repeated as the heading.</param>
    /// <param name="lifetime">How long the link is good for.</param>
    string Reset(string link, string subject, TimeSpan lifetime);
}

/// <summary>
///     Plain, self-contained HTML — no stylesheet, no images, no external anything.
/// </summary>
/// <remarks>
///     Every rule is inline because an email client is not a browser: a <c>&lt;style&gt;</c> block is
///     stripped by several of the big ones, and a linked sheet by all of them. What survives everywhere
///     is a table-free document with inline styles and a real anchor, which is what this is.
/// </remarks>
public sealed class AuthEmailBodies : IAuthEmailBodies
{
    /// <inheritdoc />
    public string Confirm(string link, string subject) =>
        Body(
            subject,
            "Confirm your email address to finish setting up your account.",
            link,
            "Confirm email",
            lifetime: null);

    /// <inheritdoc />
    public string Reset(string link, string subject, TimeSpan lifetime) =>
        Body(
            subject,
            "Somebody asked to reset the password on your account. If it was not you, ignore this "
            + "email and nothing changes.",
            link,
            "Reset password",
            lifetime);

    private static string Body(
        string heading, string lead, string link, string action, TimeSpan? lifetime) =>
        $"""
         <div style="font-family:system-ui,-apple-system,Segoe UI,sans-serif;font-size:16px;line-height:1.5;color:#18181b">
           <h1 style="font-size:20px;margin:0 0 16px">{Escape(heading)}</h1>
           <p style="margin:0 0 24px">{Escape(lead)}</p>
           <p style="margin:0 0 24px">
             <a href="{Escape(link)}" style="display:inline-block;padding:10px 18px;border-radius:6px;background:#18181b;color:#fafafa;text-decoration:none">{Escape(action)}</a>
           </p>
           {Validity(lifetime)}
           <p style="margin:0;font-size:14px;color:#52525b">If the button does not work, paste this into your browser:<br>{Escape(link)}</p>
         </div>
         """;

    /// <summary>
    ///     The "this expires" line, said the way a person would — and omitted entirely when the caller
    ///     states no lifetime, rather than printed as a bare zero.
    /// </summary>
    private static string Validity(TimeSpan? lifetime) =>
        lifetime is not { } span
            ? string.Empty
            : "<p style=\"margin:0 0 8px;font-size:14px;color:#52525b\">This link works for "
              + (span.TotalHours >= 1
                  ? $"{(int)Math.Round(span.TotalHours)} hour(s)"
                  : $"{(int)Math.Round(span.TotalMinutes)} minute(s)")
              + ".</p>";

    /// <summary>
    ///     The link carries a token straight from the caller, so it is escaped like any other untrusted
    ///     value rather than trusted because we built the URL.
    /// </summary>
    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);
}
