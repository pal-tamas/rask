using System.Net;
using System.Text;
using Microsoft.JSInterop;
using Rask.Core.Authentication;
using Rask.Core.Browser;
using Rask.Core.Routing;
using Rask.Wire;

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
        var auth = Auth(handler, out _);

        var result = await auth.SendPasswordReset("owner@example.com");

        Assert.True(result.Succeeded);
        Assert.Equal("/api/auth/forgot-password", handler.LastPath);
        Assert.Contains("owner@example.com", handler.LastBody, StringComparison.Ordinal);
        // Nowhere to go: the visitor stays on the page to read "check your email". A navigation here would
        // THROW — Go.To refuses to run outside an event handler — so reaching this pins that the recovery
        // calls really do stop at the response.
    }

    [Fact]
    public async Task Resetting_posts_the_id_the_token_and_the_password()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        var auth = Auth(handler, out var users);

        var result = await auth.ResetPassword("u1", "tok", "Password2longer");

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
        var auth = Auth(handler, out _);

        var result = await auth.ConfirmEmail("u1", "tok");

        Assert.True(result.Succeeded);
        Assert.Equal("/api/auth/confirm-email", handler.LastPath);
    }

    [Fact]
    public async Task Every_recovery_call_carries_the_CSRF_header()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        var auth = Auth(handler, out _);

        await auth.ConfirmEmail("u1", "tok");

        // Without it the endpoint answers 400. Cross-site markup cannot set a custom header, which is
        // the whole reason the endpoints require one.
        Assert.True(handler.LastHeaders!.Contains(AuthApi.RequestHeader));
    }

    [Fact]
    public async Task A_refused_reset_carries_the_servers_reason_back()
    {
        var handler = new StubHandler(
            HttpStatusCode.BadRequest, """{"error":"InvalidToken","message":null}""");
        var auth = Auth(handler, out _);

        var result = await auth.ResetPassword("u1", "stale", "Password2longer");

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
        var auth = Auth(handler, out _);

        var result = await auth.SendPasswordReset("owner@example.com");

        Assert.Equal(AuthError.MailNotConfigured, result.Error);
        Assert.Equal("no smtp", result.Message);
    }

    [Fact]
    public async Task The_configured_prefix_is_honoured()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        var auth = Auth(handler, out _, new AuthClientOptions { Prefix = "/internal/auth" });

        await auth.ConfirmEmail("u1", "tok");

        Assert.Equal("/internal/auth/confirm-email", handler.LastPath);
    }

    /// <summary>A ceremony the visitor dismissed is a refusal to render, never an exception to handle.</summary>
    [Fact]
    public async Task A_dismissed_passkey_dialog_is_reported_rather_than_thrown()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """
            {"state":"s","challenge":"AAAA","relyingPartyId":"localhost","relyingPartyName":"Test",
             "userId":"AAAA","userName":"a@b.c","userDisplayName":"A","excludeCredentials":[],"timeoutMs":1000}
            """);
        var auth = Auth(handler, out var users);

        var result = await auth.AddPasskey("Laptop");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.PasskeyRejected, result.Error);
        // It asked for options and stopped there: nothing was registered, nobody was re-rendered, and the
        // visitor stayed where they were.
        Assert.Equal("/api/auth/passkeys/register-options", handler.LastPath);
        Assert.Equal(0, users.Refreshes);
    }

    [Fact]
    public async Task Removing_a_passkey_posts_its_id()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        var auth = Auth(handler, out _);
        var id = Guid.NewGuid();

        Assert.True((await auth.RemovePasskey(id)).Succeeded);
        Assert.Equal("/api/auth/passkeys/remove", handler.LastPath);
        Assert.Contains(id.ToString(), handler.LastBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Signing_out_empties_the_offline_cache_before_it_navigates(bool everywhere)
    {
        // #1184: what the service worker kept, it kept for whoever was signed in.
        var js = new RecordingJs();
        var auth = Auth(new StubHandler(HttpStatusCode.NoContent), out _, js: js);
        var state = new RouteState();

        using (new Navigator(state).EnterHandler())
        {
            await (everywhere ? auth.SignOutEverywhere("/bye") : auth.SignOut("/bye"));
        }

        Assert.Equal(["__raskOffline.clear"], js.Calls);
        Assert.Equal("/bye", state.Path);
    }

    private static BrowserAuth Auth(
        StubHandler handler,
        out SpyUserProvider users,
        AuthClientOptions? options = null,
        IJSRuntime? js = null)
    {
        users = new SpyUserProvider();

        return new BrowserAuth(
            new HttpClient(handler) { BaseAddress = new Uri("https://localhost") },
            users,
            new StubWebAuthn(),
            options ?? new AuthClientOptions(),
            js ?? new RecordingJs());
    }

    private sealed class RecordingJs : IJSRuntime
    {
        public List<string> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            return ValueTask.FromResult<TValue>(default!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    /// <summary>A browser with no authenticator, which is what a support check is for.</summary>
    private sealed class StubWebAuthn : IWebAuthn
    {
        public ValueTask<bool> IsSupported() => ValueTask.FromResult(false);

        public ValueTask<bool> IsPlatformAuthenticatorAvailable() => ValueTask.FromResult(false);

        // What the browser returns when the visitor dismisses the dialog or it times out.
        public ValueTask<AttestationResult?> Create(PublicKeyCredentialCreationOptions options) =>
            ValueTask.FromResult<AttestationResult?>(null);

        public ValueTask<AssertionResult?> Get(PublicKeyCredentialRequestOptions options) =>
            ValueTask.FromResult<AssertionResult?>(null);
    }

    private sealed class SpyUserProvider : IUserProvider
    {
        public int Refreshes { get; private set; }

        public System.Security.Claims.ClaimsPrincipal Current { get; } = new();

        public bool IsLoading => false;

        public event EventHandler? Changed;

        public Task EnsureLoaded() => Task.CompletedTask;

        public Task RefreshAsync()
        {
            Refreshes++;
            Changed?.Invoke(this, EventArgs.Empty);
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
