using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Data;
using Rask.Tests.Shared;

namespace Rask.Auth.Tests;

/// <summary>An app wired the way the auth battery wires one, served by <c>TestServer</c>.</summary>
internal sealed class EndpointApp : IDisposable
{
    private readonly IHost _host;
    private readonly InMemorySqlite _database = new();

    /// <summary>The first-run token the app is configured with.</summary>
    public const string Token = "test-first-run-token";

    /// <param name="services">Registrations a test adds after the auth battery's.</param>
    /// <param name="endpoints">Endpoints a test maps beside <c>MapRaskAuth</c>.</param>
    /// <param name="auth">Auth options a test sets after the app's own.</param>
    /// <param name="flowContext">
    ///     Whether a request runs in the calling test's execution context, so what the test set for its own flow — a
    ///     tenant row registered with <c>CurrentRows.Use</c> — is what the request reads.
    /// </param>
    public EndpointApp(
        Action<IServiceCollection>? services = null,
        Action<IEndpointRouteBuilder>? endpoints = null,
        Action<AuthOptions>? auth = null,
        bool flowContext = false)
    {
        _host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer(o => o.PreserveExecutionContext = flowContext);
                web.ConfigureServices(registered =>
                {
                    registered.AddLogging(b => b.ClearProviders());
                    registered.AddRouting();
                    registered.AddRaskData();
                    registered.AddDbContextFactory<AuthDbContext>((sp, o) => o
                        .UseSqlite(_database.ConnectionString)
                        .AddInterceptors(sp.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor>()));
                    registered.AddSingleton(new PasswordHasher(iterations: 1_000));
                    registered.AddRaskAuth<AuthDbContext>(o =>
                    {
                        o.FirstRunToken = Token;
                        o.SignInAttemptsPerMinute = 3;
                        auth?.Invoke(o);
                    });
                    services?.Invoke(registered);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e =>
                    {
                        e.MapRaskAuth();
                        endpoints?.Invoke(e);
                    });
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
        _database.Dispose();
    }
}
