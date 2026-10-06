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
        IReadOnlyList<string> roleSets,
        IReadOnlyList<string> policies,
        IServiceProvider services)
    {
        var authenticated = user.Identity?.IsAuthenticated == true;

        // A bare [Authorize] names nothing to check below.
        if (requiresAuthentication && !authenticated)
        {
            return new ForbiddenException($"{name} requires a signed-in user, and nobody is signed in.", isAuthenticated: false);
        }

        // One set per [Authorize(Roles = …)]: any role within a set, and every set.
        if (roleSets.FirstOrDefault(roles => !InRole(user, roles)) is { } missing)
        {
            return new ForbiddenException($"{name} requires the role {missing}, which the signed-in user does not hold.", authenticated);
        }

        foreach (var policy in policies)
        {
            if (!await Meets(user, name, policy, services).ConfigureAwait(false))
            {
                return new ForbiddenException($"{name} requires the policy '{policy}', which the caller does not meet.", authenticated);
            }
        }

        return null;
    }

    private static Task<bool> Meets(ClaimsPrincipal user, string name, string policy, IServiceProvider services)
    {
        var evaluator = services.GetService<IPolicyEvaluator>()
                        ?? throw new InvalidOperationException(
                            $"'{name}' declares the policy '{policy}', but nothing is registered to decide it. "
                            + "A Rask.Server app and AddRaskCqrsServer() both register it — the alternative would "
                            + "be to ignore the policy, which is not a choice a dispatch gets to make.");

        return evaluator.Permits(user, policy);
    }

    /// <summary>Whether <paramref name="user" /> holds one of the comma-separated <paramref name="roles" />.</summary>
    internal static bool InRole(ClaimsPrincipal user, string roles) =>
        roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(user.IsInRole);
}
