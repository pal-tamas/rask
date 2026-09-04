using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Auth.Client;

/// <summary>
/// <see cref="IAuth" /> for a component running in the browser.
/// </summary>
/// <remarks>
/// The three calls are the ones a server-rendered component makes; only what happens behind them
/// differs. There is no principal to mint here and no cookie this code can write, so each flow is a
/// POST to the app's own endpoint, followed by a refresh of <see cref="IUserProvider" /> so every
/// component that reads the current user re-renders.
/// </remarks>
public sealed class BrowserAuth(
    HttpClient http,
    IUserProvider users,
    Navigator navigator,
    AuthClientOptions options) : IAuth
{
    /// <inheritdoc />
    public Task<AuthResult> RegisterAsync(
        string email, string password, string? returnUrl = null, string? firstRunToken = null) =>
        PostAsync(
            AuthApi.Register,
            new RegisterRequest(email, password, firstRunToken),
            AuthJsonContext.Default.RegisterRequest,
            returnUrl);

    /// <inheritdoc />
    public Task<AuthResult> SignInAsync(
        string email, string password, bool remember = false, string? returnUrl = null) =>
        PostAsync(
            AuthApi.Login,
            new LoginRequest(email, password, remember),
            AuthJsonContext.Default.LoginRequest,
            returnUrl);

    /// <inheritdoc />
    public async Task SignOutAsync(string? returnUrl = null)
    {
        using var request = Request(AuthApi.Logout);
        await http.SendAsync(request).ConfigureAwait(false);

        await users.RefreshAsync().ConfigureAwait(false);
        navigator.NavigateTo(LocalUrl.Sanitize(returnUrl));
    }

    /// <inheritdoc />
    public Task<AuthResult> SendPasswordResetAsync(string email) =>
        ExchangeAsync(
            AuthApi.ForgotPassword,
            new ForgotPasswordRequest(email),
            AuthJsonContext.Default.ForgotPasswordRequest);

    /// <inheritdoc />
    public Task<AuthResult> ResetPasswordAsync(string userId, string token, string password) =>
        ExchangeAsync(
            AuthApi.ResetPassword,
            new ResetPasswordRequest(userId, token, password),
            AuthJsonContext.Default.ResetPasswordRequest);

    /// <inheritdoc />
    public Task<AuthResult> ConfirmEmailAsync(string userId, string token) =>
        ExchangeAsync(
            AuthApi.ConfirmEmail,
            new ConfirmEmailRequest(userId, token),
            AuthJsonContext.Default.ConfirmEmailRequest);

    private async Task<AuthResult> PostAsync<TBody>(
        string route, TBody body, JsonTypeInfo<TBody> bodyType, string? returnUrl)
    {
        var result = await ExchangeAsync(route, body, bodyType).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return result;
        }

        await users.RefreshAsync().ConfigureAwait(false);
        navigator.NavigateTo(LocalUrl.Sanitize(returnUrl));
        return AuthResult.Success;
    }

    /// <summary>
    /// One POST and its answer, with no refresh and no navigation.
    /// </summary>
    /// <remarks>
    /// The three recovery calls stop here. None of them changes who is signed in — a reset link is used
    /// while signed out, and refreshing <see cref="IUserProvider" /> for it would re-render every
    /// component that reads the current user to arrive at the same anonymous answer.
    /// </remarks>
    private async Task<AuthResult> ExchangeAsync<TBody>(
        string route, TBody body, JsonTypeInfo<TBody> bodyType)
    {
        using var request = Request(route);
        request.Content = JsonContent.Create(body, bodyType);

        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(request).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Never reached a server. Reported as a refusal rather than thrown, so a page renders a
            // message instead of an unhandled exception on a form submit.
            return AuthResult.Fail(AuthError.InvalidCredentials);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Translate(
                    await ReadFailureAsync(response).ConfigureAwait(false));
            }
        }

        return AuthResult.Success;
    }

    private static async Task<AuthFailure?> ReadFailureAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content
                .ReadFromJsonAsync(AuthJsonContext.Default.AuthFailure)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            // A proxy or a misconfiguration can answer with something that is not this app's problem
            // document. Fall through to the generic refusal rather than failing the render.
            return null;
        }
    }

    /// <summary>Turns the endpoint's error name back into the enum the pages switch on.</summary>
    /// <remarks>
    /// The wire carries the name rather than the number so a value added later cannot silently become a
    /// different one. An unrecognised name lands on <see cref="AuthError.InvalidCredentials" />, which is
    /// the safe direction: it reports a refusal rather than a success.
    /// </remarks>
    private static AuthResult Translate(AuthFailure? failure) =>
        failure is not null && Enum.TryParse<AuthError>(failure.Error, out var error)
            ? AuthResult.Fail(error, failure.Message)
            : AuthResult.Fail(AuthError.InvalidCredentials, failure?.Message);

    private HttpRequestMessage Request(string route)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, options.Prefix + route);

        // Cross-site markup cannot set a custom header, so this is what keeps another origin from
        // driving these endpoints with the visitor's cookie.
        request.Headers.Add(AuthApi.RequestHeader, "1");
        return request;
    }
}
