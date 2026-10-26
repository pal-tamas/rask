using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;

namespace Rask.Core.Authorization;

public static class RouteAuthorizationGuard
{
    // Client-side guard redirect targets for an in-app nav to a protected route. (The initial HTTP GET
    // challenge/forbid goes through the configured auth scheme's own LoginPath/AccessDeniedPath instead.)
    // Shared by the server (RaskEndpointExtensions) and WASM (WasmLiveSession) guards so the two can't drift.
    public const string ChallengePath = "/login";
    public const string ForbidPath = "/forbidden";

    public static async Task<RouteAuthorizationResult> Evaluate(
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

        // [Guest]: a page for readers who are not signed in, which a signed-in one never sees.
        if (GuestsOnlyThen(chain) is { } then && PrincipalRights.IsSignedIn(user))
        {
            return RouteAuthorizationResult.Redirect(then);
        }

        var (authzData, failingPage) = CollectAuthorizeData(chain);
        if (authzData.Count == 0)
        {
            return RouteAuthorizationResult.Allow();
        }

        if (await AllowsAsync(services, authzData, user).ConfigureAwait(false))
        {
            return RouteAuthorizationResult.Allow();
        }

        var scheme = PickFirstScheme(authzData);
        return user.Identity?.IsAuthenticated == true
            ? RouteAuthorizationResult.Forbid(scheme, await FirstRefusedAsync(services, chain, user).ConfigureAwait(false) ?? failingPage)
            : RouteAuthorizationResult.Challenge(scheme, failingPage);
    }

    private static async Task<bool> AllowsAsync(IServiceProvider services, List<IAuthorizeData> authzData, ClaimsPrincipal user)
    {
        var policyProvider = services.GetService<IAuthorizationPolicyProvider>()
                             ?? throw new InvalidOperationException(
                                 "[Authorize] requires an IAuthorizationPolicyProvider in DI. " +
                                 "Call AddAuthorization() (server) or AddAuthorizationCore() (WASM) on the service collection.");

        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authzData).ConfigureAwait(false);
        if (policy is null)
        {
            return true;
        }

        var authzService = services.GetService<IAuthorizationService>()
                           ?? throw new InvalidOperationException(
                               "[Authorize] requires an IAuthorizationService in DI. " +
                               "Call AddAuthorization() (server) or AddAuthorizationCore() (WASM) on the service collection.");

        return (await authzService.AuthorizeAsync(user, null, policy).ConfigureAwait(false)).Succeeded;
    }

    // The outermost page a signed-in user is refused at: what every page down to it asks, asked a page at a time. The
    // layouts above it let them in, so the app draws those and the forbidden sentence in its place. Asked only once
    // the whole chain has refused, so an allowed request pays nothing for it.
    private static async Task<Type?> FirstRefusedAsync(IServiceProvider services, IReadOnlyList<Type> chain, ClaimsPrincipal user)
    {
        var asked = new List<IAuthorizeData>();
        foreach (var type in chain)
        {
            var declared = PageAuthorizeData.Of(type);
            if (declared.AllowsAnonymous)
            {
                asked.Clear();
                continue;
            }

            if (declared.Data.Length == 0)
            {
                continue;
            }

            asked.AddRange(declared.Data);
            if (!await AllowsAsync(services, asked, user).ConfigureAwait(false))
            {
                return type;
            }
        }

        return null;
    }

    // The innermost [Guest] along the chain: a page's own says where its readers go, over its layout's.
    private static string? GuestsOnlyThen(IReadOnlyList<Type> chain)
    {
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (PageAuthorizeData.Of(chain[i]).GuestsOnlyThen is { } then)
            {
                return then;
            }
        }

        return null;
    }

    // Every [Authorize] along the chain, and the outermost page that declared one. An [AllowAnonymous]
    // page resets both: nothing above it guards what it opens.
    private static (List<IAuthorizeData> Data, Type? FailingPage) CollectAuthorizeData(IReadOnlyList<Type> chain)
    {
        var authzData = new List<IAuthorizeData>();
        Type? failingPage = null;
        foreach (var type in chain)
        {
            var declared = PageAuthorizeData.Of(type);
            if (declared.AllowsAnonymous)
            {
                authzData.Clear();
                failingPage = null;
                continue;
            }

            var attrs = declared.Data;
            if (attrs.Length == 0)
            {
                continue;
            }

            authzData.AddRange(attrs);
            failingPage ??= type;
        }

        return (authzData, failingPage);
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
