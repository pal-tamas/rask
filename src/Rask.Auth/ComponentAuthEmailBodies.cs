using Rask.Auth.Pages;

namespace Rask.Auth;

/// <summary>
///     The account emails as this package has always sent them: real Rask components, rendered to a
///     standalone HTML string.
/// </summary>
/// <remarks>
///     The rendering is the only reason this cannot live in <c>Rask.Auth.Api</c> —
///     <c>Component.ToHtml()</c> is <c>Rask.Core</c>, and that package exists precisely to run where
///     Core is absent (#1069). An app on a host with no renderer gets
///     <c>Rask.Auth.AuthEmailBodies</c>'s plain HTML instead, which says the same things.
/// </remarks>
internal sealed class ComponentAuthEmailBodies : IAuthEmailBodies
{
    /// <inheritdoc />
    public string Confirm(string link, string subject) =>
        AuthEmails.Confirm(link, subject).ToHtml();

    /// <inheritdoc />
    public string Reset(string link, string subject, TimeSpan lifetime) =>
        AuthEmails.Reset(link, subject, lifetime).ToHtml();
}
