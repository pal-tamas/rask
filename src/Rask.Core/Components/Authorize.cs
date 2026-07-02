using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Core.Live;

namespace Rask.Core.Components;

/// <summary>
///     Declarative auth gating — the headless analogue of Blazor's <c>AuthorizeView</c>. Renders
///     exactly one of three slots based on the current <see cref="IUserProvider" /> (and, optionally,
///     a named authorization policy), with <b>no markup of its own</b> (transparent like
///     <see cref="Fragment" />):
///     <list type="bullet">
///         <item>
///             <see cref="Authorized" /> — the user passes the gate; the delegate receives the
///             current <see cref="ClaimsPrincipal" /> (Blazor's <c>@context.User</c>), so a greeting
///             can read <c>user.Identity!.Name</c> with no manual subscription. Falls back to the
///             children indexer when null, so <c>Authorize(Roles: "admin")[ adminPanel ]</c> renders
///             static content.
///         </item>
///         <item><see cref="NotAuthorized" /> — the user is denied (default: renders nothing).</item>
///         <item>
///             <see cref="Authorizing" /> — the provider is still loading, or a <see cref="Policy" />
///             evaluation is in flight (default: renders nothing). Bridges the anonymous→authenticated
///             flash on WASM, where the principal hydrates asynchronously.
///         </item>
///     </list>
///     <para>
///         <see cref="Roles" /> and the authenticated check are evaluated synchronously in
///         <see cref="Render" /> (no flicker). <see cref="Policy" /> is the only asynchronous vector:
///         it is evaluated via <see cref="IAuthorizationService" /> in <see cref="OnPropsChangedAsync" />
///         (and re-evaluated when the user changes), cached, and surfaced through the
///         <see cref="Authorizing" /> slot until it resolves. The component subscribes to
///         <see cref="IUserProvider.Changed" /> so a sign-in/out anywhere re-renders it.
///     </para>
/// </summary>
public sealed class Authorize : Component
{
    // null = "policy not yet evaluated" (or no policy). true/false once resolved. Read in Render to
    // gate the Policy branch and to decide whether to show the Authorizing slot.
    private bool? _policyAllowed;

    // Monotonic token: each policy evaluation captures the current value at entry; a slower in-flight
    // evaluation whose token is no longer current must not overwrite a newer verdict (avoids a
    // last-completer-wins race when the principal changes mid-flight).
    private int _policyGen;
    private IUserProvider? _provider;
    private IServiceProvider? _services;

    // The current principal from the resolved provider; never null. _provider is wired in OnMount,
    // which the lifecycle guarantees runs before OnPropsChangedAsync/Render, so this is safe at
    // every read site.
    private ClaimsPrincipal CurrentUser =>
        _provider?.Current ?? new ClaimsPrincipal(new ClaimsIdentity());

    // Transparent — Authorize itself emits nothing, it only selects one slot. (Same as the base
    // default; stated explicitly to document the headless contract.)
    protected override string? TagName => null;

    /// <summary>
    ///     Roles the gate accepts; it passes when the user is in <b>any</b> of them. Null/empty means
    ///     "any authenticated user". Pass a collection expression: <c>Authorize(Roles: ["admin", "editor"])</c>.
    /// </summary>
    public string[]? Roles { get; set; }

    /// <summary>
    ///     A named ASP.NET authorization policy, evaluated via <see cref="IAuthorizationService" />.
    ///     Composes with <see cref="Roles" /> (both must pass). Requires <c>AddAuthorization()</c>
    ///     (server) / <c>AddAuthorizationCore()</c> (WASM) in DI.
    /// </summary>
    public string? Policy { get; set; }

    /// <summary>
    ///     Rendered when the gate passes; the delegate receives the current <see cref="ClaimsPrincipal" />
    ///     so authorized markup can read the signed-in user (e.g. a name) with no manual
    ///     <see cref="IUserProvider.Changed" /> subscription — it re-runs whenever the gate re-renders.
    ///     When null, the children indexer supplies static authorized content instead:
    ///     <c>Authorize(Roles: "admin")[ adminPanel ]</c>.
    /// </summary>
    public Func<ClaimsPrincipal, Component>? Authorized { get; set; }

    /// <summary>Rendered when the gate denies. Defaults to nothing.</summary>
    public Component? NotAuthorized { get; set; }

    /// <summary>Rendered while the provider is loading or a <see cref="Policy" /> is resolving. Defaults to nothing.</summary>
    public Component? Authorizing { get; set; }

    protected override void OnMount()
    {
        _services = LiveRenderContext.Current?.Services;
        _provider = _services?.GetService<IUserProvider>();
        if (_provider is not null)
        {
            _provider.Changed += OnUserChanged;
        }
    }

    protected override void OnUnmount()
    {
        if (_provider is not null)
        {
            _provider.Changed -= OnUserChanged;
        }
    }

    protected override async Task OnPropsChangedAsync()
    {
        if (string.IsNullOrEmpty(Policy))
        {
            _policyAllowed = null;
            return;
        }

        await EvaluatePolicyAsync(CurrentUser).ConfigureAwait(false);
    }

    protected override Component? Render()
    {
        if (IsAuthorizing())
        {
            return Authorizing;
        }

        if (IsAllowed())
        {
            // The delegate runs fresh with the current principal on every re-render (the component
            // re-renders on IUserProvider.Changed), so user-dependent markup stays in sync without a
            // manual subscription. Null delegate → static authorized content via the children indexer.
            return Authorized is { } authorized
                ? authorized(CurrentUser)
                : new Fragment { Children = Children ?? [] };
        }

        return NotAuthorized;
    }

    private bool IsAuthorizing() =>
        (_provider?.IsLoading ?? false) || (!string.IsNullOrEmpty(Policy) && _policyAllowed is null);

    private bool IsAllowed()
    {
        if (CurrentUser.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (Roles is { Length: > 0 } && !InAnyRole())
        {
            return false;
        }

        if (!string.IsNullOrEmpty(Policy) && _policyAllowed != true)
        {
            return false;
        }

        return true;
    }

    private bool InAnyRole()
    {
        foreach (var role in Roles!)
        {
            if (!string.IsNullOrEmpty(role) && CurrentUser.IsInRole(role))
            {
                return true;
            }
        }

        return false;
    }

    // Re-render on sign-in/out. When a policy is in play the cached verdict is stale against the new
    // principal, so clear it and re-evaluate — showing the Authorizing slot in between.
    private void OnUserChanged()
    {
        if (!string.IsNullOrEmpty(Policy))
        {
            _policyAllowed = null;
            _ = RefreshPolicyThenRenderAsync();
            return;
        }

        StateHasChanged();
    }

    private async Task RefreshPolicyThenRenderAsync()
    {
        StateHasChanged(); // paint the Authorizing slot immediately
        await EvaluatePolicyAsync(CurrentUser).ConfigureAwait(false);
        StateHasChanged(); // paint the resolved verdict
    }

    private async Task EvaluatePolicyAsync(ClaimsPrincipal principal)
    {
        // Capture the generation at entry (synchronously, before any await). A newer evaluation will
        // bump it; this one then drops its result rather than clobbering the fresher verdict.
        var gen = ++_policyGen;
        bool allowed;
        try
        {
            // Resolve the named policy through the provider, then authorize against its requirements —
            // the same path RouteAuthorizationGuard uses for [Authorize(Policy = ...)].
            var policyProvider = _services?.GetService<IAuthorizationPolicyProvider>();
            var authz = _services?.GetService<IAuthorizationService>();
            if (policyProvider is null || authz is null)
            {
                allowed = false;
            }
            else
            {
                var policy = await policyProvider.GetPolicyAsync(Policy!).ConfigureAwait(false);
                if (policy is null)
                {
                    allowed = false;
                }
                else
                {
                    var result = await authz.AuthorizeAsync(principal, null, policy).ConfigureAwait(false);
                    allowed = result.Succeeded;
                }
            }
        }
        catch
        {
            // Fail closed: a throw from policy resolution/evaluation denies rather than leaving the gate
            // stuck on the Authorizing slot. (This path also runs from a fire-and-forget continuation in
            // OnUserChanged, where an unobserved throw would otherwise be swallowed silently.)
            allowed = false;
        }

        if (gen == _policyGen)
        {
            _policyAllowed = allowed;
        }
    }
}
