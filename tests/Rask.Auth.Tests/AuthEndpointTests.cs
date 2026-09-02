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
using Rask.Core.Authentication;

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
                        services.AddDbContextFactory<AuthDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
                        services.AddRaskAuth<AuthDbContext>(o => o.FirstRunToken = Token);
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

            using var scope = _host.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureCreated();
        }

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
