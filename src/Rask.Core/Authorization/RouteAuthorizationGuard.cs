using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Authorization;

public static class RouteAuthorizationGuard
{
    // Client-side guard redirect targets for an in-app nav to a protected route. (The initial HTTP GET
    // challenge/forbid goes through the configured auth scheme's own LoginPath/AccessDeniedPath instead.)
    // Shared by the server (RaskEndpointExtensions) and WASM (WasmLiveSession) guards so the two can't drift.
    public const string ChallengePath = "/login";
    public const string ForbidPath = "/forbidden";

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

        var authzData = CollectAuthorizeData(chain, out var failingPage);
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

        var authzResult = await authzService.AuthorizeAsync(user, null, policy).ConfigureAwait(false);
        if (authzResult.Succeeded)
        {
            return RouteAuthorizationResult.Allow();
        }

        var scheme = PickFirstScheme(authzData);
        return user.Identity?.IsAuthenticated == true
            ? RouteAuthorizationResult.Forbid(scheme, failingPage)
            : RouteAuthorizationResult.Challenge(scheme, failingPage);
    }

    /// <summary>
    ///     Whether reaching the page at the end of <paramref name="chain" /> takes any authorization at all:
    ///     an <c>[Authorize]</c> somewhere on the chain that no later <c>[AllowAnonymous]</c> clears.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Decided from the attributes alone, without evaluating a policy against anyone. That is the
    ///         question a copy of a page shared between visitors needs answered, and it is deliberately the
    ///         strict one: a page any policy guards stays out, even when an anonymous visitor would happen to
    ///         satisfy the policy today, because what a policy admits can change without the page changing.
    ///     </para>
    ///     <para>
    ///         Reads the chain with the same walk <see cref="EvaluateAsync" /> uses, so the two cannot disagree
    ///         about which pages are guarded.
    ///     </para>
    /// </remarks>
    internal static bool RequiresAuthorization(IReadOnlyList<Type> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return chain.Count > 0 && CollectAuthorizeData(chain, out _).Count > 0;
    }

    // Walks the chain outermost first: an [AllowAnonymous] clears what the pages above it demanded, and a
    // page below it may demand something again.
    private static List<IAuthorizeData> CollectAuthorizeData(IReadOnlyList<Type> chain, out Type? failingPage)
    {
        var authzData = new List<IAuthorizeData>();
        failingPage = null;
        foreach (var type in chain)
        {
            if (type.GetCustomAttribute<AllowAnonymousAttribute>(true) is not null)
            {
                authzData.Clear();
                failingPage = null;
                continue;
            }

            var attrs = type.GetCustomAttributes(true).OfType<IAuthorizeData>().ToArray();
            if (attrs.Length == 0)
            {
                continue;
            }

            authzData.AddRange(attrs);
            failingPage ??= type;
        }

        return authzData;
    }

    private static string? PickFirstScheme(IReadOnlyList<IAuthorizeData> data) => (from t in data
                                                                                   select t.AuthenticationSchemes
        into schemes
                                                                                   where !string.IsNullOrEmpty(schemes)
                                                                                   select schemes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        into first
                                                                                   where first.Length > 0
                                                                                   select first[0]).FirstOrDefault();
}
