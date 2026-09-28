using System.Security.Claims;
using Rask.Wire;

namespace Rask.Auth;

/// <summary>
/// The account store, without its user type.
/// </summary>
/// <remarks>
/// The endpoints are mapped by <c>MapRaskAuth()</c>, which has no way to know which account type an app declared — it is a
/// parameterless extension method on the endpoint builder, chosen so an app writes one line. Registering this alongside
/// the generic service gives the endpoints something to resolve that does not name the type.
/// </remarks>
internal interface IAccounts
{
    Task<AccountOutcome> RegisterAsync(
        string email,
        string password,
        string? firstRunToken,
        string? client,
        CancellationToken cancellationToken = default);

    Task<AccountOutcome> ValidateAsync(
        string email, string password, string? client, CancellationToken cancellationToken = default);

    Task<AuthResult> SendPasswordResetAsync(
        string email, string? client, CancellationToken cancellationToken = default);

    Task<AuthResult> ResetPasswordAsync(
        string userId, string token, string password, CancellationToken cancellationToken = default);

    Task<AuthResult> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken = default);

    Task<int> SignOutOtherDevicesAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);

    Task<int> SignOutEverywhereAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);

    Task<PasskeyCreationChallenge?> BeginAddPasskeyAsync(
        Guid userId, string? origin, CancellationToken cancellationToken = default);

    /// <summary>Why <see cref="BeginAddPasskeyAsync" /> gave no challenge, for the message a person sees.</summary>
    Task<AuthError> PasskeyRefusalAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<AuthResult> CompleteAddPasskeyAsync(
        Guid userId, PasskeyRegistrationRequest request, string? origin, CancellationToken cancellationToken = default);

    PasskeyRequestChallenge? BeginPasskeySignIn(string? origin);

    Task<AccountOutcome> CompletePasskeySignInAsync(
        PasskeyLoginRequest request, string? origin, string? client, CancellationToken cancellationToken = default);

    Task<AuthResult> RemovePasskeyAsync(Guid userId, Guid passkeyId, CancellationToken cancellationToken = default);
}
