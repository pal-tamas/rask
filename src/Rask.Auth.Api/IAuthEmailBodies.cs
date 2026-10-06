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

    /// <summary>
    ///     The "somebody tried to register with your address" body, sent to an account's owner in place of
    ///     telling the person registering that the address is taken.
    /// </summary>
    /// <param name="link">The absolute link to the sign-in page.</param>
    /// <param name="subject">The subject, repeated as the heading.</param>
    string AlreadyRegistered(string link, string subject);
}
