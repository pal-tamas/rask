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

    /// <inheritdoc cref="IAuth.SignInWithPasskey(bool, string?)" />
    public static Task<AuthResult> SignInWithPasskey(bool remember = false, string? returnUrl = null) =>
        Resolve().SignInWithPasskey(remember, returnUrl);

    /// <inheritdoc cref="IAuth.SignOut(string?)" />
    public static Task SignOut(string? returnUrl = null) => Resolve().SignOut(returnUrl);

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

    private static IAuth Resolve() =>
        (Ambient.Services ?? throw new InvalidOperationException(
            "Auth was called outside any work in progress — an event handler, a render or a request — so there is "
            + "no session to sign in or out. Inject IAuth there instead."))
        .GetRequiredService<IAuth>();
}
