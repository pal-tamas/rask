using System.ComponentModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Server;

/// <summary>
///     Decides a handler's <c>[Authorize(Policy = …)]</c> with ASP.NET's <see cref="IAuthorizationService" />, for
///     the endpoint and for a local dispatch alike. Registered by <c>AddRaskCqrsServer()</c>.
/// </summary>
/// <param name="services">The scope the request is dispatched in.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class AuthorizationPolicyEvaluator(IServiceProvider services) : IPolicyEvaluator
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No authorization services are registered.</exception>
    public async Task<bool> Permits(ClaimsPrincipal user, string policy)
    {
        var authorization = services.GetService<IAuthorizationService>()
                            ?? throw new InvalidOperationException(
                                $"A handler declares the policy '{policy}', but no authorization services are "
                                + "registered. Call AddAuthorization() during startup — the alternative would be "
                                + "to ignore the policy, which is not a choice a dispatch gets to make.");

        return (await authorization.AuthorizeAsync(user, policy).ConfigureAwait(false)).Succeeded;
    }
}
