using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Data;
using Rask.Wire;

namespace Rask.Auth.Tests;

/// <summary>
///     The <c>/api/auth</c> endpoints over real HTTP.
/// </summary>
/// <remarks>
///     These are the contract every host that is not C# speaks — a TypeScript front end, a meta
///     framework's Node process, a WebAssembly client. Testing them through <c>TestServer</c> rather
///     than by calling the handlers keeps the parts that only exist over HTTP honest: the cookie, the
///     status codes, and the header the CSRF defence depends on.
/// </remarks>
[Collection(AuthDbCollection.Name)]
public sealed class AuthEndpointTests
{
    private const string Password = "Password1";
    private const string Token = "test-first-run-token";

    [Fact]
    public async Task Me_answers_no_content_when_nobody_is_signed_in()
    {
        using var app = new EndpointApp();
        using var client = app.Client();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Registering_signs_in_and_says_who_you_are()
    {
        using var app = new EndpointApp();
        using var client = app.Client();

        var response = await Post(client, "/api/auth/register", new
        {
            email = "owner@example.com",
            password = Password,
            firstRunToken = Token,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("owner@example.com", me.GetProperty("email").GetString());

        var roles = me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Contains(RaskRoles.Admin, roles);
    }

    [Fact]
    public async Task The_cookie_from_registering_carries_into_the_next_request()
    {
        using var app = new EndpointApp();
        using var client = app.Client();

        await Post(client, "/api/auth/register", new
        {
            email = "owner@example.com",
            password = Password,
            firstRunToken = Token,
        });

        // A separate request, authenticated only by the cookie the previous one set. This is the whole
        // mechanism the browser hosts rely on.
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_request_without_the_csrf_header_is_refused_and_says_which_header()
    {
        using var app = new EndpointApp();
        using var client = app.Client();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { email = "a@example.com", password = Password }),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MissingRequestHeader", body.GetProperty("error").GetString());
        Assert.Contains(RaskAuthDefaults.RequestHeader, body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_wrong_password_is_a_401_carrying_the_code_and_no_detail()
    {
        using var app = new EndpointApp();
        using var client = app.Client();

        await Post(client, "/api/auth/register", new
        {
            email = "owner@example.com",
            password = Password,
            firstRunToken = Token,
        });

        var response = await Post(client, "/api/auth/login", new
        {
            email = "owner@example.com",
            password = "WrongPassword1",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(nameof(AuthError.InvalidCredentials), body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Signing_out_takes_the_session_with_it()
    {
        using var app = new EndpointApp();
        using var client = app.Client();

        await Post(client, "/api/auth/register", new
        {
            email = "owner@example.com",
            password = Password,
            firstRunToken = Token,
        });

        var logout = await Post(client, "/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.NoContent, me.StatusCode);
    }

    /// <summary>The whole passkey round-trip over HTTP: options, register, sign out, options, sign in.</summary>
    /// <remarks>
    /// The part that only exists over HTTP is the point of testing it here: the options endpoints need the session
    /// cookie and the CSRF header, and the sign-in has to end on a cookie that the NEXT request is accepted with.
    /// </remarks>
    [Fact]
    public async Task A_passkey_registers_over_http_and_then_signs_somebody_in()
    {
        using var app = new EndpointApp();
        using var client = app.Client();

        var registered = await Post(client, "/api/auth/register", new
        {
            email = "owner@example.com",
            password = Password,
            firstRunToken = Token,
        });

        var userId = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var options = await Post(client, "/api/auth/passkeys/register-options", null);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);

        var challenge = await options.Content.ReadFromJsonAsync<JsonElement>();
        var relyingPartyId = challenge.GetProperty("relyingPartyId").GetString()!;
        var origin = "https://" + relyingPartyId;

        using var authenticator = new TestAuthenticator();
        var credential = authenticator.Register(
            relyingPartyId,
            origin,
            Base64Url.DecodeFromChars(challenge.GetProperty("challenge").GetString()!),
            challenge.GetProperty("state").GetString()!,
            "Test key");

        var added = await Post(client, "/api/auth/passkeys/register", new
        {
            state = credential.State,
            name = credential.Name,
            rawId = credential.RawId,
            clientDataJson = credential.ClientDataJson,
            attestationObject = credential.AttestationObject,
            transports = credential.Transports,
        });

        Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);

        await Post(client, "/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/auth/me")).StatusCode);

        var loginOptions = await Post(client, "/api/auth/passkeys/login-options", null);
        Assert.Equal(HttpStatusCode.OK, loginOptions.StatusCode);

        var loginChallenge = await loginOptions.Content.ReadFromJsonAsync<JsonElement>();
        authenticator.SignCount = 1;

        var assertion = authenticator.SignIn(
            relyingPartyId,
            origin,
            Base64Url.DecodeFromChars(loginChallenge.GetProperty("challenge").GetString()!),
            Guid.Parse(userId),
            loginChallenge.GetProperty("state").GetString()!);

        var signedIn = await Post(client, "/api/auth/passkeys/login", new
        {
            state = assertion.State,
            rawId = assertion.RawId,
            clientDataJson = assertion.ClientDataJson,
            authenticatorData = assertion.AuthenticatorData,
            signature = assertion.Signature,
            userHandle = assertion.UserHandle,
            remember = true,
        });

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal(
            "owner@example.com",
            (await signedIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("email").GetString());

        // The cookie the sign-in set is what the next request is accepted with, exactly as for a password.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        // And it started a session row, so the passkey sign-in shows up on the device list like any other.
        await using var db = app.NewContext();
        Assert.True(await db.Set<Session>().AnyAsync(session => session.UserId == Guid.Parse(userId)));
    }

    [Fact]
    public async Task Adding_a_passkey_needs_a_session()
    {
        using var app = new EndpointApp();
        using var client = app.Client();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await Post(client, "/api/auth/passkeys/register-options", null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await Post(client, "/api/auth/passkeys/remove", new { id = Guid.NewGuid().ToString() })).StatusCode);
    }

    /// <summary>Anonymous, because signing in is what it is for — but still behind the CSRF header.</summary>
    [Fact]
    public async Task Passkey_sign_in_options_are_anonymous_but_need_the_header()
    {
        using var app = new EndpointApp();
        using var client = app.Client();

        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/api/auth/passkeys/login-options", null)).StatusCode);

        using var bare = new HttpRequestMessage(HttpMethod.Post, "/api/auth/passkeys/login-options");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(bare)).StatusCode);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(RaskAuthDefaults.RequestHeader, "1");

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    /// <summary>An app wired the way the auth battery wires one, served by <c>TestServer</c>.</summary>
    [Fact]
    public async Task Signing_in_starts_a_session_row_and_signing_out_ends_it()
    {
        using var app = new EndpointApp();
        using var client = app.Client();

        await RegisterOwnerAsync(client);
        Assert.Equal(1, await ActiveSessionsAsync(app));

        var logout = await Post(client, "/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        Assert.Equal(0, await ActiveSessionsAsync(app));
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Signing_out_other_devices_signs_them_out_on_their_next_request()
    {
        using var app = new EndpointApp();
        using var laptop = app.Client();
        using var phone = app.Client();

        await RegisterOwnerAsync(laptop);
        var login = await Post(phone, "/api/auth/login", new { email = "owner@example.com", password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/auth/me")).StatusCode);

        var others = await Post(laptop, "/api/auth/logout-other-devices", new { });
        Assert.Equal(HttpStatusCode.NoContent, others.StatusCode);

        // The phone still holds its cookie, and it is no longer honoured.
        Assert.Equal(HttpStatusCode.NoContent, (await phone.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Remember_me_is_what_makes_the_cookie_outlive_the_browser()
    {
        using var app = new EndpointApp();
        using var client = app.Client();
        await RegisterOwnerAsync(client);

        var forgetting = await Post(app.Client(), "/api/auth/login", new { email = "owner@example.com", password = Password, remember = false });
        var remembering = await Post(app.Client(), "/api/auth/login", new { email = "owner@example.com", password = Password, remember = true });

        Assert.DoesNotContain("expires=", SetCookie(forgetting), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", SetCookie(remembering), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Too_many_wrong_passwords_answer_429_so_a_client_can_say_wait()
    {
        using var app = new EndpointApp();
        using var client = app.Client();
        await RegisterOwnerAsync(client);

        using var guesser = app.Client();
        for (var i = 0; i < 3; i++)
        {
            await Post(guesser, "/api/auth/login", new { email = "owner@example.com", password = "WrongPassword1" });
        }

        var response = await Post(guesser, "/api/auth/login", new { email = "owner@example.com", password = Password });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(nameof(AuthError.TooManyAttempts), body.GetProperty("error").GetString());
    }

    private static async Task RegisterOwnerAsync(HttpClient client)
    {
        var response = await Post(client, "/api/auth/register", new
        {
            email = "owner@example.com",
            password = Password,
            firstRunToken = Token,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<int> ActiveSessionsAsync(EndpointApp app)
    {
        await using var db = app.NewContext();
        return await db.Set<Session>().CountAsync();
    }

    private static string SetCookie(HttpResponseMessage response) =>
        string.Join("; ", response.Headers.TryGetValues("Set-Cookie", out var values) ? values : []);

    private sealed class EndpointApp : IDisposable
    {
        private readonly IHost _host;
        private readonly string _dbPath;

        public EndpointApp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"rask-auth-endpoints-{Guid.NewGuid():N}.db");

            _host = new HostBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.ConfigureServices(services =>
                    {
                        services.AddLogging(b => b.ClearProviders());
                        services.AddRouting();
                        services.AddRaskData(o => o.DispatchDomainEventsInProcess = false);
                        services.AddDbContextFactory<AuthDbContext>((sp, o) => o
                            .UseSqlite($"Data Source={_dbPath};Pooling=False")
                            .AddInterceptors(sp.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor>()));
                        services.AddSingleton(new PasswordHasher(iterations: 1_000));
                        services.AddRaskAuth<AuthDbContext>(o =>
                        {
                            o.FirstRunToken = Token;
                            o.SignInAttemptsPerMinute = 3;
                        });
                    });
                    web.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(e => e.MapRaskAuth());
                    });
                })
                .Start();

            using var db = NewContext();
            db.Database.EnsureCreated();
        }

        public AuthDbContext NewContext() =>
            _host.Services.GetRequiredService<IDbContextFactory<AuthDbContext>>().CreateDbContext();

        /// <summary>A client that keeps cookies, over https.</summary>
        /// <remarks>
        ///     <para>
        ///         <c>TestServer</c>'s own client does <b>not</b> keep cookies — its handler has no
        ///         container — so a session issued by one request would never reach the next one and every
        ///         follow-up would look signed-out. The handler below supplies one.
        ///     </para>
        ///     <para>
        ///         https, not http: the auth cookie is issued with <c>Secure</c>, and a cookie container
        ///         honours that — over http it would accept the <c>Set-Cookie</c> and then decline to send
        ///         it back, which is the same symptom for a different reason.
        ///     </para>
        /// </remarks>
        public HttpClient Client() =>
            new(new CookieHandler(_host.GetTestServer().CreateHandler()))
            {
                BaseAddress = new Uri("https://localhost"),
            };

        /// <summary>Carries cookies between requests, the way a browser does.</summary>
        private sealed class CookieHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
        {
            private readonly CookieContainer _cookies = new();

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var uri = request.RequestUri!;
                var header = _cookies.GetCookieHeader(uri);

                if (!string.IsNullOrEmpty(header))
                {
                    request.Headers.Add("Cookie", header);
                }

                var response = await base.SendAsync(request, cancellationToken);

                if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                {
                    foreach (var value in setCookies)
                    {
                        _cookies.SetCookies(uri, value);
                    }
                }

                return response;
            }
        }

        public void Dispose()
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            try
            {
                File.Delete(_dbPath);
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a test over.
            }
        }
    }
}
