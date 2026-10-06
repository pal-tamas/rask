using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

// The one authorization decision, for the HTTP endpoint and for a local dispatch alike.
internal static class RequestAccess
{
    /// <summary>
    ///     Why <paramref name="user" /> may not send <paramref name="name" />, or null when they may.
    /// </summary>
    /// <exception cref="InvalidOperationException">A policy is declared and nothing is registered to decide it.</exception>
    internal static async ValueTask<ForbiddenException?> Refusal(
        ClaimsPrincipal user,
        string name,
        bool requiresAuthentication,
        string? roles,
        string? policy,
        IServiceProvider services)
    {
        var authenticated = user.Identity?.IsAuthenticated == true;

        // A bare [Authorize] names nothing to check below.
        if (requiresAuthentication && !authenticated)
        {
            return new ForbiddenException($"{name} requires a signed-in user, and nobody is signed in.", isAuthenticated: false);
        }

        if (roles is { Length: > 0 } && !InRole(user, roles))
        {
            return new ForbiddenException($"{name} requires the role {roles}, which the signed-in user does not hold.", authenticated);
        }

        if (policy is { Length: > 0 })
        {
            var evaluator = services.GetService<IPolicyEvaluator>()
                            ?? throw new InvalidOperationException(
                                $"'{name}' declares the policy '{policy}', but nothing is registered to decide it. "
                                + "A Rask.Server app and AddRaskCqrsServer() both register it — the alternative would "
                                + "be to ignore the policy, which is not a choice a dispatch gets to make.");

            if (!await evaluator.Permits(user, policy).ConfigureAwait(false))
            {
                return new ForbiddenException($"{name} requires the policy '{policy}', which the caller does not meet.", authenticated);
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="user" /> holds one of the comma-separated <paramref name="roles" />.</summary>
    internal static bool InRole(ClaimsPrincipal user, string roles) =>
        roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(user.IsInRole);
}
