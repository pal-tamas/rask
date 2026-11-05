using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Wire;

namespace Rask.Core;

/// <summary>
///     Register, sign in, sign out — from an event handler or a form submit, with nothing injected:
/// </summary>
/// <remarks>
///     <code>
///     await Auth.SignIn(email, password);
///     await Auth.Register(email, password, returnUrl: "/welcome");
///     await Auth.SignOut();
///     </code>
///     <para>
///         Each call is <see cref="IAuth" />'s, on the host the work is running on — the Server host validates against
///         the account store, the browser posts to the app's <c>/api/auth</c> endpoints. Outside any work in progress
///         (a constructor, a static initializer, a background thread) there is no session to act on: inject
///         <see cref="IAuth" /> there instead.
///     </para>
/// </remarks>
public static class Auth
{
    /// <inheritdoc cref="IAuth.Register(string, string, string?, string?)" />
    public static Task<AuthResult> Register(
        string email, string password, string? returnUrl = null, string? firstRunToken = null) =>
        Resolve().Register(email, password, returnUrl, firstRunToken);

    /// <inheritdoc cref="IAuth.Register{TUser}(string, string, Action{TUser}, string?, string?)" />
    public static Task<AuthResult> Register<TUser>(
        string email,
        string password,
        Action<TUser> apply,
        string? returnUrl = null,
        string? firstRunToken = null)
        where TUser : class =>
        Resolve().Register(email, password, apply, returnUrl, firstRunToken);

    /// <inheritdoc cref="IAuth.SignIn(string, string, bool, string?)" />
    public static Task<AuthResult> SignIn(
        string email, string password, bool remember = false, string? returnUrl = null) =>
        Resolve().SignIn(email, password, remember, returnUrl);

    /// <summary>
    ///     Signs <paramref name="principal" /> in once the current handler finishes — an app that checked the credentials
    ///     itself, on a host whose authentication scheme can sign a principal in (a cookie scheme).
    /// </summary>
    /// <param name="principal">Who is signing in.</param>
    /// <param name="returnUrl">Where to land afterwards. Sanitized to a local URL before it is used.</param>
    public static Task SignIn(ClaimsPrincipal principal, string? returnUrl = null) =>
        Handover().SignIn(principal, returnUrl);

    /// <inheritdoc cref="IAuth.SignInWithPasskey(bool, string?)" />
    public static Task<AuthResult> SignInWithPasskey(bool remember = false, string? returnUrl = null) =>
        Resolve().SignInWithPasskey(remember, returnUrl);

    /// <inheritdoc cref="IAuthSignIn.SignInWithToken(string, string?)" />
    public static Task SignInWithToken(string token, string? returnUrl = null) =>
        Handover().SignInWithToken(token, returnUrl);

    /// <summary>Signs the current visitor out — through <see cref="IAuth" /> when the app has accounts, else the
    /// session alone (<see cref="IAuthSignIn.SignOut(string?, string?)" />), which clears the session cookie.</summary>
    /// <param name="returnUrl">Where to land afterwards. Sanitized to a local URL before it is used.</param>
    public static Task SignOut(string? returnUrl = null)
    {
        var services = Work();
        return services.GetService<IAuth>() is { } auth
            ? auth.SignOut(returnUrl)
            : services.GetRequiredService<IAuthSignIn>().SignOut(returnUrl);
    }

    /// <inheritdoc cref="IAuth.SignOutOtherDevices" />
    public static Task SignOutOtherDevices() => Resolve().SignOutOtherDevices();

    /// <inheritdoc cref="IAuth.SignOutEverywhere(string?)" />
    public static Task SignOutEverywhere(string? returnUrl = null) => Resolve().SignOutEverywhere(returnUrl);

    /// <inheritdoc cref="IAuth.SendPasswordReset(string)" />
    public static Task<AuthResult> SendPasswordReset(string email) => Resolve().SendPasswordReset(email);

    /// <inheritdoc cref="IAuth.ResetPassword(string, string, string)" />
    public static Task<AuthResult> ResetPassword(string userId, string token, string password) =>
        Resolve().ResetPassword(userId, token, password);

    /// <inheritdoc cref="IAuth.ConfirmEmail(string, string)" />
    public static Task<AuthResult> ConfirmEmail(string userId, string token) =>
        Resolve().ConfirmEmail(userId, token);

    /// <inheritdoc cref="IAuth.AddPasskey(string?)" />
    public static Task<AuthResult> AddPasskey(string? name = null) => Resolve().AddPasskey(name);

    /// <inheritdoc cref="IAuth.RemovePasskey(Guid)" />
    public static Task<AuthResult> RemovePasskey(Guid id) => Resolve().RemovePasskey(id);

    /// <inheritdoc cref="IAuth.ActInTenant(Guid, string?)" />
    /// <remarks>
    /// In an app that signs its users in itself, the tenant is handed to the choice <c>MapRaskTenantChoice</c> mapped,
    /// which writes it into the app's own sign-in; the page then loads again at <paramref name="returnUrl" />.
    /// </remarks>
    public static Task<AuthResult> ActInTenant(Guid tenant, string? returnUrl = null) =>
        Work().GetService<IAuth>() is { } auth
            ? auth.ActInTenant(tenant, returnUrl)
            : OwnSignIn().ChooseTenant(tenant.ToString(), returnUrl);

    /// <inheritdoc cref="IAuth.ActInTenant(long, string?)" />
    /// <remarks>
    /// In an app that signs its users in itself, the number is handed to the choice <c>MapRaskTenantChoice</c> mapped.
    /// </remarks>
    public static Task<AuthResult> ActInTenant(long tenant, string? returnUrl = null)
    {
        if (Work().GetService<IAuth>() is { } auth)
        {
            return auth.ActInTenant(tenant, returnUrl);
        }

        return tenant > 0
            ? OwnSignIn().ChooseTenant(tenant.ToString(CultureInfo.InvariantCulture), returnUrl)
            : Task.FromResult(AuthResult.Fail(AuthError.NotAllowed, "Tenants are numbered from one."));
    }

    /// <inheritdoc cref="IAuth.LeaveTenant(string?)" />
    public static Task<AuthResult> LeaveTenant(string? returnUrl = null) =>
        Work().GetService<IAuth>() is { } auth ? auth.LeaveTenant(returnUrl) : OwnSignIn().ChooseTenant(null, returnUrl);

    private static IAuth Resolve() => Work().GetRequiredService<IAuth>();

    private static IAuthSignIn Handover() => Work().GetRequiredService<IAuthSignIn>();

    // The Server host's sign-in relay, for an app with no IAuth: it signs its users in itself.
    private static AuthSignIn OwnSignIn() =>
        Work().GetService<AuthSignIn>()
        ?? throw new NotSupportedException(
            "Choosing a tenant with your own sign-in goes through the Server host's sign-in relay. In WebAssembly, POST " +
            "{ \"tenant\": \"…\" } with the X-Rask-Auth header to the path app.MapRaskTenantChoice(…) mapped.");

    private static IServiceProvider Work() => Ambient.Require("Auth", "Inject IAuth there instead.");
}
