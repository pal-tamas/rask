using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Server;

/// <summary>
///     The <see cref="ISessionRevalidator" /> <c>AddSessionRevalidator&lt;T&gt;()</c> registers: the app's own, with its
///     answers kept.
/// </summary>
internal sealed class KeptSessionRevalidator(ISessionRevalidator app, KeptSessionAnswers answers) : ISessionRevalidator
{
    public ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return answers.Answer(principal, app, cancellationToken);
    }
}
