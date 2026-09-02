using System.Net.Http.Json;
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
        PostAsync(AuthApi.Register, new RegisterRequest(email, password, firstRunToken), returnUrl);

    /// <inheritdoc />
    public Task<AuthResult> SignInAsync(
        string email, string password, bool remember = false, string? returnUrl = null) =>
        PostAsync(AuthApi.Login, new LoginRequest(email, password, remember), returnUrl);

    /// <inheritdoc />
    public async Task SignOutAsync(string? returnUrl = null)
    {
        using var request = Request(AuthApi.Logout);
        await http.SendAsync(request).ConfigureAwait(false);

        await users.RefreshAsync().ConfigureAwait(false);
        navigator.NavigateTo(LocalUrl.Sanitize(returnUrl));
    }

    private async Task<AuthResult> PostAsync<TBody>(string route, TBody body, string? returnUrl)
    {
        using var request = Request(route);
        request.Content = JsonContent.Create(body);

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

        await users.RefreshAsync().ConfigureAwait(false);
        navigator.NavigateTo(LocalUrl.Sanitize(returnUrl));
        return AuthResult.Success;
    }

    private static async Task<AuthFailure?> ReadFailureAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<AuthFailure>().ConfigureAwait(false);
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
