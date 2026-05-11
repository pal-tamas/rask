using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Authorization;

public static class RouteAuthorizationGuard
{
    public static async Task<RouteAuthorizationResult> EvaluateAsync(
        IServiceProvider services,
        IReadOnlyList<Type> chain,
        ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(user);

        if (chain.Count == 0)
        {
            return RouteAuthorizationResult.Allow();
        }

        var authzData = new List<IAuthorizeData>();
        Type? failingPage = null;
        foreach (var type in chain)
        {
            if (type.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null)
            {
                authzData.Clear();
                failingPage = null;
                continue;
            }

            var attrs = type.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().ToArray();
            if (attrs.Length == 0)
            {
                continue;
            }

            authzData.AddRange(attrs);
            failingPage ??= type;
        }

        if (authzData.Count == 0)
        {
            return RouteAuthorizationResult.Allow();
        }

        var policyProvider = services.GetService<IAuthorizationPolicyProvider>()
                             ?? throw new InvalidOperationException(
                                 "[Authorize] requires an IAuthorizationPolicyProvider in DI. " +
                                 "Call AddAuthorization() (server) or AddAuthorizationCore() (WASM) on the service collection.");

        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authzData).ConfigureAwait(false);
        if (policy is null)
        {
            return RouteAuthorizationResult.Allow();
        }

        var authzService = services.GetService<IAuthorizationService>()
                           ?? throw new InvalidOperationException(
                               "[Authorize] requires an IAuthorizationService in DI. " +
                               "Call AddAuthorization() (server) or AddAuthorizationCore() (WASM) on the service collection.");

        var authzResult = await authzService.AuthorizeAsync(user, resource: null, policy).ConfigureAwait(false);
        if (authzResult.Succeeded)
        {
            return RouteAuthorizationResult.Allow();
        }

        var scheme = PickFirstScheme(authzData);
        return user.Identity?.IsAuthenticated == true
            ? RouteAuthorizationResult.Forbid(scheme, failingPage)
            : RouteAuthorizationResult.Challenge(scheme, failingPage);
    }

    private static string? PickFirstScheme(IReadOnlyList<IAuthorizeData> data) => (from t in data select t.AuthenticationSchemes into schemes where !string.IsNullOrEmpty(schemes) select schemes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) into first where first.Length > 0 select first[0]).FirstOrDefault();
}
