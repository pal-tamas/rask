using System.Net;
using System.Text;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Auth.Client.Tests;

/// <summary>
///     The three recovery flows, from the browser half.
/// </summary>
/// <remarks>
///     What is worth pinning here is not that a POST happens — it is the two things a browser client can
///     silently get wrong and still look green: the route it posts to, and whether it drags
///     <see cref="IUserProvider" /> and the navigator along behind it. None of these three changes who is
///     signed in, so both would be work done for nothing, and a refresh would re-render every component
///     that reads the current user to arrive at the same anonymous answer.
/// </remarks>
public sealed class BrowserAuthRecoveryTests
{
    [Fact]
    public async Task Asking_for_a_reset_posts_to_forgot_password()
    {
        var handler = new StubHandler(HttpStatusCode.Accepted);
        var auth = Auth(handler, out _, out var route);

        var result = await auth.SendPasswordResetAsync("owner@example.com");

        Assert.True(result.Succeeded);
        Assert.Equal("/api/auth/forgot-password", handler.LastPath);
        Assert.Contains("owner@example.com", handler.LastBody, StringComparison.Ordinal);

        // Nowhere to go: the visitor stays on the page to read "check your email". A navigation here
        // would also THROW — Navigator refuses to run outside an event handler — so this pins that the
        // recovery calls really do stop at the response.
        Assert.Equal("/", route.Path);
    }

    [Fact]
    public async Task Resetting_posts_the_id_the_token_and_the_password()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        var auth = Auth(handler, out var users, out _);

        var result = await auth.ResetPasswordAsync("u1", "tok", "Password2longer");

        Assert.True(result.Succeeded);
        Assert.Equal("/api/auth/reset-password", handler.LastPath);
        Assert.Contains("\"userId\":\"u1\"", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"token\":\"tok\"", handler.LastBody, StringComparison.Ordinal);

        // A successful reset does not sign anybody in, so there is nothing to refresh.
        Assert.Equal(0, users.Refreshes);
    }

    [Fact]
    public async Task Confirming_posts_to_confirm_email()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        var auth = Auth(handler, out _, out _);

        var result = await auth.ConfirmEmailAsync("u1", "tok");

        Assert.True(result.Succeeded);
        Assert.Equal("/api/auth/confirm-email", handler.LastPath);
    }

    [Fact]
    public async Task Every_recovery_call_carries_the_CSRF_header()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        var auth = Auth(handler, out _, out _);

        await auth.ConfirmEmailAsync("u1", "tok");

        // Without it the endpoint answers 400. Cross-site markup cannot set a custom header, which is
        // the whole reason the endpoints require one.
        Assert.True(handler.LastHeaders!.Contains(AuthApi.RequestHeader));
    }

    [Fact]
    public async Task A_refused_reset_carries_the_servers_reason_back()
    {
        var handler = new StubHandler(
            HttpStatusCode.BadRequest, """{"error":"InvalidToken","message":null}""");

        var auth = Auth(handler, out _, out _);

        var result = await auth.ResetPasswordAsync("u1", "stale", "Password2longer");

        // The name rather than the number, so a value added later cannot silently become a different
        // one — and "ask for a new link" is a different instruction from "pick a longer password".
        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.InvalidToken, result.Error);
    }

    [Fact]
    public async Task An_app_with_no_mail_battery_is_reported_as_such()
    {
        var handler = new StubHandler(
            HttpStatusCode.ServiceUnavailable, """{"error":"MailNotConfigured","message":"no smtp"}""");

        var auth = Auth(handler, out _, out _);

        var result = await auth.SendPasswordResetAsync("owner@example.com");

        Assert.Equal(AuthError.MailNotConfigured, result.Error);
        Assert.Equal("no smtp", result.Message);
    }

    [Fact]
    public async Task The_configured_prefix_is_honoured()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        var auth = Auth(handler, out _, out _, new AuthClientOptions { Prefix = "/internal/auth" });

        await auth.ConfirmEmailAsync("u1", "tok");

        Assert.Equal("/internal/auth/confirm-email", handler.LastPath);
    }

    private static BrowserAuth Auth(
        StubHandler handler,
        out SpyUserProvider users,
        out RouteState route,
        AuthClientOptions? options = null)
    {
        users = new SpyUserProvider();
        route = new RouteState();

        return new BrowserAuth(
            new HttpClient(handler) { BaseAddress = new Uri("https://localhost") },
            users,
            new Navigator(route),
            options ?? new AuthClientOptions());
    }

    private sealed class SpyUserProvider : IUserProvider
    {
        public int Refreshes { get; private set; }

        public System.Security.Claims.ClaimsPrincipal Current { get; } = new();

        public bool IsLoading => false;

        public event Action? Changed;

        public Task EnsureLoadedAsync() => Task.CompletedTask;

        public Task RefreshAsync()
        {
            Refreshes++;
            Changed?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class StubHandler(HttpStatusCode status, string? json = null) : HttpMessageHandler
    {
        public string? LastPath { get; private set; }

        public string LastBody { get; private set; } = "";

        public System.Net.Http.Headers.HttpRequestHeaders? LastHeaders { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri?.AbsolutePath;
            LastHeaders = request.Headers;

            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var response = new HttpResponseMessage(status);

            if (json is not null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return response;
        }
    }
}
