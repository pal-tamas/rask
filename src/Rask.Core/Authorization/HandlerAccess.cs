using System.ComponentModel;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Core.Diagnostics;
using Rask.Core.Live;

namespace Rask.Core.Authorization;

/// <summary>
///     Asks whether the person acting now may run a component method that carries <c>[Authorize]</c>. Machinery:
///     the generated code that guards such a method calls it, and a method group handed to a callback is guarded
///     the same way. Put <c>[Authorize]</c> on the method instead of calling this.
/// </summary>
/// <remarks>
///     The principal is the session's as the event arrives — the live session asks again on every event — and a
///     refusal throws, so the method's body never runs and neither does what its caller wrote after it. The
///     dispatch turns that into a toast; the attempt is logged at Information, naming the method and nothing of
///     the person.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class HandlerAccess
{
    private static readonly ClaimsPrincipal Nobody = new(new ClaimsIdentity());

    /// <summary>Returns when the current principal meets <paramref name="handler" />'s <c>[Authorize]</c>; throws when it does not.</summary>
    /// <param name="handler">The guarded method.</param>
    public static ValueTask Demand(MethodInfo handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return HandlerRules.Of(handler) is { } rule ? Demand(rule, HandlerRules.Name(handler)) : default;
    }

    /// <summary>
    ///     <see cref="Demand(MethodInfo)" /> for a method that returns no task: it cannot wait, so a policy that only
    ///     answers asynchronously refuses it.
    /// </summary>
    /// <param name="handler">The guarded method.</param>
    public static void DemandNow(MethodInfo handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (HandlerRules.Of(handler) is { } rule)
        {
            DemandNow(rule, HandlerRules.Name(handler));
        }
    }

    internal static async ValueTask Demand(IAuthorizeData[] rule, string handler)
    {
        if (!await Allows(rule).ConfigureAwait(false))
        {
            throw Refuse(handler, null);
        }
    }

    internal static void DemandNow(IAuthorizeData[] rule, string handler)
    {
        var answer = Allows(rule);
        if (answer.IsCompletedSuccessfully)
        {
            if (!answer.Result)
            {
                throw Refuse(handler, null);
            }

            return;
        }

        // Observed rather than dropped; Evaluate never throws.
        _ = answer.AsTask();
        throw Refuse(handler, " Its policy answers asynchronously, which a method that returns no Task cannot wait for.");
    }

    // Fails closed: outside a session, or in a host with no authorization services, nobody meets a rule.
    private static ValueTask<bool> Allows(IAuthorizeData[] rule)
    {
        if (AmbientServices.Current is not { } services
            || services.GetService<IAuthorizationPolicyProvider>() is not { } policies
            || services.GetService<IAuthorizationService>() is not { } authorization)
        {
            return new ValueTask<bool>(false);
        }

        var user = services.GetService<IUserProvider>()?.Current ?? Nobody;
        return Evaluate(rule, user, policies, authorization);
    }

    private static async ValueTask<bool> Evaluate(
        IAuthorizeData[] rule, ClaimsPrincipal user, IAuthorizationPolicyProvider policies, IAuthorizationService authorization)
    {
        try
        {
            var policy = await AuthorizationPolicy.CombineAsync(policies, rule).ConfigureAwait(false);
            return policy is null || (await authorization.AuthorizeAsync(user, null, policy).ConfigureAwait(false)).Succeeded;
        }
#pragma warning disable CA1031 // A policy that throws refuses: the method guarded by it must not run on a failed check.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Handler", "Rask: an [Authorize] check on a handler threw; it refuses.", ex);
            return false;
        }
    }

    private static HandlerForbiddenException Refuse(string handler, string? why)
    {
        var refused = new HandlerForbiddenException(handler);
        RaskDiagnostics.Report(RaskLogLevel.Information, "Rask.Handler", "Rask: " + refused.Message + why);
        return refused;
    }
}
