using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Wire;

namespace Rask.Auth.Tests;

/// <summary>
///     <see cref="IBearerSignIn" />: an app keeps its own sign-in contract — a partner's <c>POST /api/auth</c> answering
///     the token as text — and Rask checks the password, throttles, renews old hashes and starts the session the token names.
/// </summary>
public sealed class BearerSignInTests
{
    private const string Password = "Password1";
    private const string Owner = "owner@example.com";
    private const string Key = "a-signing-key-long-enough-for-hmac-sha256!";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_token_an_app_s_own_endpoint_hands_out_opens_an_endpoint_that_takes_bearer_tokens_only()
    {
        using var app = PartnerApp();
        using var client = app.Client();
        await RegisterOwnerAsync(client);
        using var partner = app.Client();

        var token = await PartnerSignInAsync(partner, Owner, Password);
        using var withToken = await BearerOnlyAsync(partner, token);
        using var withoutToken = await BearerOnlyAsync(partner, token: null);

        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
        Assert.Equal(Owner, await withToken.Content.ReadAsStringAsync(Cancel));
        Assert.Equal(HttpStatusCode.Unauthorized, withoutToken.StatusCode);
    }

    [Fact]
    public async Task Signing_out_everywhere_ends_the_token_with_its_session()
    {
        using var app = PartnerApp();
        using var browser = app.Client();
        await RegisterOwnerAsync(browser);
        using var partner = app.Client();
        var token = await PartnerSignInAsync(partner, Owner, Password);

        using var ended = await PostAsync(browser, AuthApi.LogoutEverywhere, new { });
        using var afterwards = await BearerOnlyAsync(partner, token);

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_is_refused_with_no_token_and_no_session()
    {
        await using var harness = await ClaimedAsync();
        var before = await SessionCountAsync(harness);

        var issue = await BearerSignIn(harness).SignIn(Owner, "wrong-password", "203.0.113.7", Cancel);

        Assert.Equal(AuthError.InvalidCredentials, issue.Result.Error);
        Assert.Null(issue.Token);
        Assert.Null(issue.ExpiresAt);
        Assert.Equal(before, await SessionCountAsync(harness));
    }

    [Fact]
    public async Task An_unknown_name_is_refused_the_same_way_as_a_wrong_password()
    {
        await using var harness = await ClaimedAsync();

        var issue = await BearerSignIn(harness).SignIn("nobody@example.com", Password, "203.0.113.7", Cancel);

        Assert.Equal(AuthError.InvalidCredentials, issue.Result.Error);
        Assert.Null(issue.Token);
    }

    [Fact]
    public async Task A_client_past_the_limit_is_throttled_even_with_the_right_password()
    {
        await using var harness = await ClaimedAsync(o => o.SignInAttemptsPerMinute = 2);
        var signIn = BearerSignIn(harness);
        await signIn.SignIn(Owner, "wrong-1", "203.0.113.7", Cancel);
        await signIn.SignIn(Owner, "wrong-2", "203.0.113.7", Cancel);

        var throttled = await signIn.SignIn(Owner, Password, "203.0.113.7", Cancel);
        var elsewhere = await signIn.SignIn(Owner, Password, "198.51.100.9", Cancel);

        Assert.Equal(AuthError.TooManyAttempts, throttled.Result.Error);
        Assert.Null(throttled.Token);
        Assert.True(elsewhere.Result.Succeeded);
    }

    [Theory]
    [InlineData('a')]
    [InlineData('b')]
    public async Task A_bcrypt_hash_carried_over_signs_in_and_is_renewed_to_pbkdf2(char revision)
    {
        await using var harness = await ClaimedAsync();
        await StoreHashAsync(harness, Owner, BCrypt.Net.BCrypt.HashPassword(Password, BCrypt.Net.BCrypt.GenerateSalt(10, revision)));

        var issue = await BearerSignIn(harness).SignIn(Owner, Password, "203.0.113.7", Cancel);

        Assert.True(issue.Result.Succeeded, $"{issue.Result.Error} {issue.Result.Message}");
        Assert.NotNull(issue.Token);
        Assert.StartsWith("$rask$pbkdf2-sha256$", (await harness.UserAsync(Owner))!.PasswordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_session_lasts_the_token_s_lifetime_and_using_the_token_does_not_extend_it()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero));
        await using var harness = await ClaimedAsync(o => o.BearerLifetime = TimeSpan.FromHours(1), clock);
        var issue = await BearerSignIn(harness).SignIn(Owner, Password, "203.0.113.7", Cancel);
        var sessionId = await OnlyTokenSessionAsync(harness);
        var sessions = harness.Services.GetRequiredService<IAuthSessions>();

        clock.Advance(TimeSpan.FromMinutes(40));
        var used = await sessions.ResumeForTokenAsync(sessionId, Cancel);
        var expiresAfterUse = (await SessionAsync(harness, sessionId)).ExpiresAt.Instant;
        clock.Advance(TimeSpan.FromMinutes(21));
        var lapsed = await sessions.ResumeForTokenAsync(sessionId, Cancel);

        Assert.Equal(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero), issue.ExpiresAt);
        Assert.NotNull(used);
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero), expiresAfterUse);
        Assert.False((await SessionAsync(harness, sessionId)).Persistent);
        Assert.Null(lapsed);
    }

    [Fact]
    public async Task The_sweep_removes_a_token_s_session_a_day_after_it_expires()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        await using var harness = await ClaimedAsync(o => o.BearerLifetime = TimeSpan.FromHours(1), clock);
        await BearerSignIn(harness).SignIn(Owner, Password, "203.0.113.7", Cancel);
        var sessionId = await OnlyTokenSessionAsync(harness);
        var sweep = harness.Services.GetServices<IHostedService>().OfType<SessionSweep<AuthDbContext>>().Single();

        clock.Advance(TimeSpan.FromHours(2));
        await sweep.SweepAsync(Cancel);
        var keptForADay = await RowExistsAsync(harness, sessionId);
        clock.Advance(TimeSpan.FromDays(1));
        await sweep.SweepAsync(Cancel);

        Assert.True(keptForADay);
        Assert.False(await RowExistsAsync(harness, sessionId));
    }

    [Fact]
    public async Task Without_bearer_on_it_says_what_to_configure()
    {
        await using var harness = await ClaimedAsync(o => o.Bearer = false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BearerSignIn(harness).SignIn(Owner, Password, client: null, Cancel));

        Assert.Contains("Rask:Auth:BearerSigningKey", error.Message, StringComparison.Ordinal);
    }

    // An app with Rask's sign-in and a legacy partner contract beside it: POST /partner/auth answers the token as text, and
    // GET /partner/me takes bearer tokens alone.
    private static EndpointApp PartnerApp() =>
        new(
            endpoints: e =>
            {
                e.MapPost("/partner/auth", async (Credentials body, HttpContext http, IBearerSignIn bearer) =>
                    await bearer.SignIn(body.Username, body.Password, AuthClient.Of(http)) is { Token: { } token }
                        ? Results.Text(token)
                        : Results.Unauthorized());
                e.MapGet("/partner/me", (HttpContext http) => http.User.Identity!.Name)
                    .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
            },
            auth: o =>
            {
                o.Bearer = true;
                o.BearerSigningKey = Key;
            });

    private static async Task RegisterOwnerAsync(HttpClient client)
    {
        using var registered = await PostAsync(
            client, AuthApi.Register, new { email = Owner, password = Password, firstRunToken = EndpointApp.Token });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
    }

    private static async Task<string> PartnerSignInAsync(HttpClient client, string name, string password)
    {
        using var response = await client.PostAsJsonAsync("/partner/auth", new Credentials(name, password), Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Cancel);
    }

    private static async Task<HttpResponseMessage> BearerOnlyAsync(HttpClient client, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/partner/me");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request, Cancel);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string route, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthApi.DefaultPrefix + route)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(RaskAuthDefaults.RequestHeader, "1");

        return await client.SendAsync(request, Cancel);
    }

    private static IBearerSignIn BearerSignIn(AuthHarness harness) => harness.Services.GetRequiredService<IBearerSignIn>();

    private static async Task<AuthHarness> ClaimedAsync(Action<AuthOptions>? configure = null, TimeProvider? clock = null)
    {
        var harness = new AuthHarness(
            o =>
            {
                o.Bearer = true;
                o.BearerSigningKey = Key;
                configure?.Invoke(o);
            },
            clock: clock);
        await harness.StartAsync();
        var claimed = await harness.Services.GetRequiredService<IAccountFlows>()
            .RegisterAsync(Owner, Password, AuthHarness.FirstRunTokenValue, client: null, Cancel);
        Assert.True(claimed.Result.Succeeded, $"{claimed.Result.Error} {claimed.Result.Message}");

        return harness;
    }

    internal static async Task StoreHashAsync(AuthHarness harness, string email, string hash)
    {
        await using var db = harness.NewContext();
        var user = await db.Set<TestUser>().SingleAsync(u => u.Email == email, Cancel);
        user.Rehash(hash);
        await db.SaveChangesAsync(Cancel);
    }

    private static async Task<int> SessionCountAsync(AuthHarness harness)
    {
        await using var db = harness.NewContext();
        return await db.Set<Session>().IgnoreQueryFilters().CountAsync(Cancel);
    }

    // The session the token sign-in started: the only one, since registering through the service issues none.
    private static async Task<Guid> OnlyTokenSessionAsync(AuthHarness harness)
    {
        await using var db = harness.NewContext();
        return await db.Set<Session>().IgnoreQueryFilters().Select(s => s.Id).SingleAsync(Cancel);
    }

    private static async Task<Session> SessionAsync(AuthHarness harness, Guid id)
    {
        await using var db = harness.NewContext();
        return await db.Set<Session>().IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == id, Cancel);
    }

    private static async Task<bool> RowExistsAsync(AuthHarness harness, Guid id)
    {
        await using var db = harness.NewContext();
        return await db.Set<Session>().IgnoreQueryFilters().AnyAsync(s => s.Id == id, Cancel);
    }

    private sealed record Credentials(string Username, string Password);
}
